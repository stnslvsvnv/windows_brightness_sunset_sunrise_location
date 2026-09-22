using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NotebookAutoBrightness;

internal sealed class AutomationController
{
    private const int MinTransitionMinutes = 0;
    private const int MaxTransitionMinutes = 40;
    private static readonly TimeSpan LocationRefreshInterval = TimeSpan.FromMinutes(30);

    private readonly string _executablePath;

    private AppSettings _settings = new();
    private bool _isApplying;
    private int? _lastAppliedBrightness;
    private LocationResult? _lastLocation;
    private DateTime _lastResolvedLocationAtUtc = DateTime.MinValue;
    private SunTimes? _cachedSunTimes;
    private DateTime _cachedSunDate = DateTime.MinValue;
    private double? _cachedLat;
    private double? _cachedLon;

    public AutomationController(string executablePath)
    {
        _executablePath = executablePath;
        CurrentStatus = CreateInitialStatus();
    }

    public event Action<AutomationStatus>? StatusChanged;

    public event Action? SettingsChanged;

    public AutomationStatus CurrentStatus { get; private set; }

    public AppSettings GetSettings() => CloneSettings(_settings);

    public async Task InitializeAsync()
    {
        LoadSettings();
        PublishStatus(CreateInitialStatus());
        await ApplyScheduleAsync(false);
    }

    public void UpdateSettings(AppSettings settings)
    {
        var normalized = NormalizeSettings(settings);
        var previousUseGeolocation = _settings.UseGeolocation;
        var previousCity = _settings.City ?? string.Empty;

        _settings = normalized;
        ApplyAutoStartPreference();

        if (previousUseGeolocation != _settings.UseGeolocation ||
            !string.Equals(previousCity, _settings.City, StringComparison.Ordinal))
        {
            InvalidateLocationCache();
        }

        SettingsStore.Save(_settings);
        SettingsChanged?.Invoke();
        PublishStatus(BuildStatusFromCurrentState());
    }

    public void ToggleEnabled()
    {
        var settings = GetSettings();
        settings.Enabled = !settings.Enabled;
        UpdateSettings(settings);
    }

    public async Task<AutomationStatus> ApplyScheduleAsync(bool showMessages, IWin32Window? owner = null)
    {
        if (_isApplying)
        {
            return CurrentStatus;
        }

        _isApplying = true;
        try
        {
            if (!_settings.Enabled)
            {
                var paused = new AutomationStatus(
                    Brightness: null,
                    Phase: null,
                    NextChange: null,
                    ThemeLabel: GetCurrentThemeLabel(),
                    ScheduleSource: "Automation off",
                    StatusSummary: "Automation paused.",
                    StatusDetail: "Brightness and theme changes are off until you enable automation.",
                    LocationLabel: BuildLocationLabel(),
                    SunWindowLabel: BuildSunWindowLabel(null));
                PublishStatus(paused);
                return paused;
            }

            var now = DateTime.Now;
            SunTimes? sunTimes = null;
            var scheduleSource = "Manual schedule";

            if (_settings.UseSunSchedule)
            {
                var location = await ResolveLocationAsync(showMessages, owner);
                if (location != null)
                {
                    sunTimes = await GetSunTimesAsync(location, showMessages, owner);
                    scheduleSource = sunTimes == null ? "Manual schedule fallback" : "Sunrise and sunset";
                }
                else
                {
                    scheduleSource = "Manual schedule (location required)";
                }
            }

            var brightnessEvaluation = ScheduleCalculator.EvaluateBrightness(
                now,
                sunTimes,
                _settings.DayStartTime,
                _settings.NightStartTime,
                _settings.DayBrightness,
                _settings.NightBrightness,
                TimeSpan.FromMinutes(ClampTransitionMinutes(_settings.TransitionMinutes)));

            string themeLabel;
            if (_settings.AutoThemeSwitching)
            {
                var useLightTheme = ScheduleCalculator.ShouldUseLightTheme(
                    now,
                    sunTimes,
                    _settings.DayStartTime,
                    _settings.NightStartTime,
                    TimeSpan.FromMinutes(ClampTransitionMinutes(_settings.ThemeSwitchLeadMinutes)));
                var targetTheme = useLightTheme ? WindowsThemeController.ThemeMode.Light : WindowsThemeController.ThemeMode.Dark;
                if (WindowsThemeController.GetCurrentTheme() != targetTheme)
                {
                    _ = WindowsThemeController.SetTheme(targetTheme);
                }

                themeLabel = useLightTheme ? "Light" : "Dark";
            }
            else
            {
                themeLabel = GetCurrentThemeLabel();
            }

            if (_lastAppliedBrightness != brightnessEvaluation.Brightness)
            {
                if (!BrightnessController.TrySetBrightness(brightnessEvaluation.Brightness, out var error))
                {
                    if (showMessages)
                    {
                        ShowMessage(
                            owner,
                            $"Failed to set brightness. {error}",
                            AppRuntime.AppName,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }

                    var failed = new AutomationStatus(
                        Brightness: _lastAppliedBrightness,
                        Phase: brightnessEvaluation.Phase,
                        NextChange: brightnessEvaluation.NextChange,
                        ThemeLabel: themeLabel,
                        ScheduleSource: scheduleSource,
                        StatusSummary: "Brightness update failed.",
                        StatusDetail: "Your display driver or hardware rejected the change.",
                        LocationLabel: BuildLocationLabel(),
                        SunWindowLabel: BuildSunWindowLabel(sunTimes));
                    PublishStatus(failed);
                    return failed;
                }

                _lastAppliedBrightness = brightnessEvaluation.Brightness;
            }

            var statusSummary = _settings.AutoThemeSwitching
                ? $"{ScheduleCalculator.GetPhaseLabel(brightnessEvaluation.Phase)} mode, {themeLabel} theme."
                : $"{ScheduleCalculator.GetPhaseLabel(brightnessEvaluation.Phase)} mode.";
            var statusDetail = $"{scheduleSource}. Next change at {brightnessEvaluation.NextChange:HH:mm}.";

            var applied = new AutomationStatus(
                Brightness: brightnessEvaluation.Brightness,
                Phase: brightnessEvaluation.Phase,
                NextChange: brightnessEvaluation.NextChange,
                ThemeLabel: themeLabel,
                ScheduleSource: scheduleSource,
                StatusSummary: statusSummary,
                StatusDetail: statusDetail,
                LocationLabel: BuildLocationLabel(),
                SunWindowLabel: BuildSunWindowLabel(sunTimes));
            PublishStatus(applied);
            return applied;
        }
        finally
        {
            _isApplying = false;
        }
    }

    private void LoadSettings()
    {
        _settings = NormalizeSettings(SettingsStore.Load());
        _settings.StartWithWindows = IsAutoStartEnabled();
        SettingsStore.Save(_settings);
        SettingsChanged?.Invoke();
    }

    private AutomationStatus CreateInitialStatus() =>
        new(
            Brightness: _lastAppliedBrightness,
            Phase: null,
            NextChange: null,
            ThemeLabel: GetCurrentThemeLabel(),
            ScheduleSource: _settings.UseSunSchedule ? "Sunrise and sunset" : "Manual schedule",
            StatusSummary: "Starting automation...",
            StatusDetail: "Loading settings and preparing the tray controller.",
            LocationLabel: BuildLocationLabel(),
            SunWindowLabel: BuildSunWindowLabel(null));

    private AutomationStatus BuildStatusFromCurrentState() =>
        new(
            Brightness: CurrentStatus.Brightness,
            Phase: CurrentStatus.Phase,
            NextChange: CurrentStatus.NextChange,
            ThemeLabel: CurrentStatus.ThemeLabel,
            ScheduleSource: _settings.Enabled
                ? (_settings.UseSunSchedule ? "Sunrise and sunset" : "Manual schedule")
                : "Automation off",
            StatusSummary: CurrentStatus.StatusSummary,
            StatusDetail: CurrentStatus.StatusDetail,
            LocationLabel: BuildLocationLabel(),
            SunWindowLabel: BuildSunWindowLabel(GetCachedSunTimesForPreview()));

    private async Task<LocationResult?> ResolveLocationAsync(bool showMessages, IWin32Window? owner)
    {
        if (CanReuseResolvedLocation())
        {
            return _lastLocation;
        }

        if (_settings.UseGeolocation)
        {
            var ipLocation = await GeoService.TryGetIpLocationAsync();
            if (ipLocation != null)
            {
                RememberLocation(ipLocation);
                return ipLocation;
            }
        }

        if (!string.IsNullOrWhiteSpace(_settings.City))
        {
            var cityLocation = await GeoService.TryGeocodeCityAsync(_settings.City);
            if (cityLocation != null)
            {
                RememberLocation(cityLocation);
                return cityLocation;
            }
        }

        if (_settings.LastLatitude.HasValue && _settings.LastLongitude.HasValue)
        {
            _lastLocation = new LocationResult(
                _settings.LastLatitude.Value,
                _settings.LastLongitude.Value,
                _settings.LastCity ?? string.Empty,
                _settings.LastCountry ?? string.Empty,
                "Last known");
            return _lastLocation;
        }

        if (showMessages)
        {
            ShowMessage(
                owner,
                "Location is required to calculate sunrise and sunset. Please enter a city.",
                AppRuntime.AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        return null;
    }

    private async Task<SunTimes?> GetSunTimesAsync(LocationResult location, bool showMessages, IWin32Window? owner)
    {
        if (_cachedSunTimes != null &&
            _cachedSunDate.Date == DateTime.Today &&
            _cachedLat == location.Latitude &&
            _cachedLon == location.Longitude)
        {
            return _cachedSunTimes;
        }

        var sunTimes = await SunService.TryGetSunTimesAsync(location.Latitude, location.Longitude, DateTime.Today);
        if (sunTimes != null)
        {
            _cachedSunTimes = sunTimes;
            _cachedSunDate = DateTime.Today;
            _cachedLat = location.Latitude;
            _cachedLon = location.Longitude;
            return sunTimes;
        }

        if (showMessages)
        {
            ShowMessage(
                owner,
                "Unable to get sunrise and sunset from the server. Manual schedule will be used.",
                AppRuntime.AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        return null;
    }

    private void RememberLocation(LocationResult location)
    {
        _lastLocation = location;
        _lastResolvedLocationAtUtc = DateTime.UtcNow;
        SaveLastLocation(location);
    }

    private void SaveLastLocation(LocationResult location)
    {
        _settings.LastLatitude = location.Latitude;
        _settings.LastLongitude = location.Longitude;
        _settings.LastCity = location.City;
        _settings.LastCountry = location.Country;
        SettingsStore.Save(_settings);
    }

    private void InvalidateLocationCache()
    {
        _lastLocation = null;
        _lastResolvedLocationAtUtc = DateTime.MinValue;
        _cachedSunTimes = null;
        _cachedSunDate = DateTime.MinValue;
        _cachedLat = null;
        _cachedLon = null;
    }

    private bool CanReuseResolvedLocation() =>
        _lastLocation != null &&
        !string.Equals(_lastLocation.Source, "Last known", StringComparison.Ordinal) &&
        DateTime.UtcNow - _lastResolvedLocationAtUtc < LocationRefreshInterval;

    private SunTimes? GetCachedSunTimesForPreview() =>
        _settings.UseSunSchedule &&
        _cachedSunTimes != null &&
        _cachedSunDate.Date == DateTime.Today
            ? _cachedSunTimes
            : null;

    private string BuildLocationLabel()
    {
        if (_lastLocation != null)
        {
            return FormatLocationLabel(
                _lastLocation.City,
                _lastLocation.Country,
                _lastLocation.Latitude,
                _lastLocation.Longitude,
                _lastLocation.Source);
        }

        if (_settings.LastLatitude.HasValue && _settings.LastLongitude.HasValue)
        {
            return FormatLocationLabel(
                _settings.LastCity,
                _settings.LastCountry,
                _settings.LastLatitude.Value,
                _settings.LastLongitude.Value,
                "Last known");
        }

        return "No location yet. Add a city or allow geolocation to unlock sunrise and sunset mode.";
    }

    private string BuildSunWindowLabel(SunTimes? sunTimes) =>
        sunTimes != null
            ? $"Sunrise {sunTimes.Sunrise:HH:mm}  |  Sunset {sunTimes.Sunset:HH:mm}"
            : $"Manual window {_settings.DayStartTime:hh\\:mm}  |  Night starts {_settings.NightStartTime:hh\\:mm}";

    private static string FormatLocationLabel(string? city, string? country, double latitude, double longitude, string source)
    {
        var place = $"{city} {country}".Trim();
        if (string.IsNullOrWhiteSpace(place))
        {
            place = "Unknown location";
        }

        return $"{place}  |  {latitude:F4}, {longitude:F4}  |  via {source}";
    }

    private static string GetCurrentThemeLabel()
    {
        var currentTheme = WindowsThemeController.GetCurrentTheme();
        return currentTheme switch
        {
            WindowsThemeController.ThemeMode.Dark => "Dark",
            WindowsThemeController.ThemeMode.Light => "Light",
            _ => "System"
        };
    }

    private void ApplyAutoStartPreference()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (key == null)
        {
            return;
        }

        if (_settings.StartWithWindows)
        {
            key.SetValue(AppRuntime.AutoRunValueName, AppRuntime.BuildAutoStartCommand(_executablePath));
            return;
        }

        key.DeleteValue(AppRuntime.AutoRunValueName, false);
    }

    private static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
        return key?.GetValue(AppRuntime.AutoRunValueName) != null;
    }

    private void PublishStatus(AutomationStatus status)
    {
        CurrentStatus = status;
        StatusChanged?.Invoke(status);
    }

    private static AppSettings NormalizeSettings(AppSettings settings) =>
        new()
        {
            Enabled = settings.Enabled,
            UseGeolocation = settings.UseGeolocation,
            UseSunSchedule = settings.UseSunSchedule,
            StartWithWindows = settings.StartWithWindows,
            AutoThemeSwitching = settings.AutoThemeSwitching,
            DayBrightness = Clamp(settings.DayBrightness),
            NightBrightness = Clamp(settings.NightBrightness),
            TransitionMinutes = ClampTransitionMinutes(settings.TransitionMinutes),
            ThemeSwitchLeadMinutes = ClampTransitionMinutes(settings.ThemeSwitchLeadMinutes),
            City = settings.City?.Trim() ?? string.Empty,
            DayStartTime = settings.DayStartTime,
            NightStartTime = settings.NightStartTime,
            LastLatitude = settings.LastLatitude,
            LastLongitude = settings.LastLongitude,
            LastCity = settings.LastCity,
            LastCountry = settings.LastCountry
        };

    private static AppSettings CloneSettings(AppSettings settings) =>
        new()
        {
            Enabled = settings.Enabled,
            UseGeolocation = settings.UseGeolocation,
            UseSunSchedule = settings.UseSunSchedule,
            StartWithWindows = settings.StartWithWindows,
            AutoThemeSwitching = settings.AutoThemeSwitching,
            DayBrightness = settings.DayBrightness,
            NightBrightness = settings.NightBrightness,
            TransitionMinutes = settings.TransitionMinutes,
            ThemeSwitchLeadMinutes = settings.ThemeSwitchLeadMinutes,
            City = settings.City,
            DayStartTime = settings.DayStartTime,
            NightStartTime = settings.NightStartTime,
            LastLatitude = settings.LastLatitude,
            LastLongitude = settings.LastLongitude,
            LastCity = settings.LastCity,
            LastCountry = settings.LastCountry
        };

    private static void ShowMessage(
        IWin32Window? owner,
        string text,
        string caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon)
    {
        if (owner != null)
        {
            MessageBox.Show(owner, text, caption, buttons, icon);
            return;
        }

        MessageBox.Show(text, caption, buttons, icon);
    }

    private static int ClampTransitionMinutes(int value) => Math.Clamp(value, MinTransitionMinutes, MaxTransitionMinutes);

    private static int Clamp(int value) => Math.Clamp(value, 0, 100);
}
