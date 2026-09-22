using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NotebookAutoBrightness;

internal sealed class AutomationController
{
    private const int MinTransitionMinutes = 0;
    private const int MaxTransitionMinutes = 40;
    private const int BrightnessTolerancePercent = 2;
    private const int VerifyIntervalTicks = 4;
    private const double CoordinateTolerance = 0.01;
    private const string LastKnownLocationSource = "Last known";
    internal const string SunScheduleSource = "Sunrise and sunset";
    internal const string SunScheduleSourceCached = "Sunrise and sunset (cached)";
    internal const string ManualScheduleSource = "Manual schedule";
    private static readonly TimeSpan LocationRefreshInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan LastKnownLocationTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan SunTimesRetryInterval = TimeSpan.FromMinutes(10);

    private readonly string _executablePath;

    private AppSettings _settings = new();
    private bool _isApplying;
    private int? _lastAppliedBrightness;
    private int _tickCount;
    private bool _reassertBrightnessOnNextTick;
    private LocationResult? _lastLocation;
    private DateTime _lastResolvedLocationAtUtc = DateTime.MinValue;
    private SunTimes? _cachedSunTimes;
    private DateTime _cachedSunDate = DateTime.MinValue;
    private double? _cachedLat;
    private double? _cachedLon;
    private SunTimes? _lastKnownSunTimes;
    private DateTime _sunTimesRetryAfterUtc = DateTime.MinValue;
    private bool _usingStaleSunTimes;

    public AutomationController(string executablePath)
    {
        _executablePath = executablePath;
        CurrentStatus = CreateInitialStatus();
    }

    public event Action<AutomationStatus>? StatusChanged;

    public event Action? SettingsChanged;

    // Raised right after the Windows theme was switched, so the host can re-apply the brightness
    // once the display stack has settled.
    public event Action? ThemeChanged;

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
        var normalized = PreserveLastKnownLocation(NormalizeSettings(settings), _settings);
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

    public async Task<AutomationStatus> ApplyScheduleAsync(bool showMessages, IWin32Window? owner = null, bool forceBrightness = false)
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

            SunTimes? sunTimes = null;
            var scheduleSource = ManualScheduleSource;

            if (_settings.UseSunSchedule)
            {
                var location = await ResolveLocationAsync(showMessages, owner);
                if (location != null)
                {
                    sunTimes = await GetSunTimesAsync(location, showMessages, owner);
                    scheduleSource = sunTimes == null
                        ? "Manual schedule fallback"
                        : _usingStaleSunTimes
                            ? SunScheduleSourceCached
                            : SunScheduleSource;
                }
                else
                {
                    scheduleSource = "Manual schedule (location required)";
                }
            }

            var now = DateTime.Now;

            var themeLead = TimeSpan.FromMinutes(ClampTransitionMinutes(_settings.ThemeSwitchLeadMinutes));
            var shiftSchedule = _settings.AutoThemeSwitching && themeLead > TimeSpan.Zero;
            var brightnessSunTimes = shiftSchedule && sunTimes != null
                ? ScheduleCalculator.ShiftForThemeLead(sunTimes, themeLead)
                : sunTimes;

            var brightnessEvaluation = ScheduleCalculator.EvaluateBrightness(
                now,
                brightnessSunTimes,
                shiftSchedule ? _settings.DayStartTime - themeLead : _settings.DayStartTime,
                shiftSchedule ? _settings.NightStartTime - themeLead : _settings.NightStartTime,
                _settings.DayBrightness,
                _settings.NightBrightness,
                TimeSpan.FromMinutes(ClampTransitionMinutes(_settings.TransitionMinutes)));

            var themeChanged = false;
            string themeLabel;
            if (_settings.AutoThemeSwitching)
            {
                var useLightTheme = ScheduleCalculator.ShouldUseLightTheme(
                    now,
                    sunTimes,
                    _settings.DayStartTime,
                    _settings.NightStartTime,
                    themeLead);
                var targetTheme = useLightTheme ? WindowsThemeController.ThemeMode.Light : WindowsThemeController.ThemeMode.Dark;
                themeChanged = WindowsThemeController.EnsureTheme(targetTheme);
                themeLabel = useLightTheme ? "Light" : "Dark";
            }
            else
            {
                themeLabel = GetCurrentThemeLabel();
            }

            _tickCount++;
            var reassertBrightness = forceBrightness || _reassertBrightnessOnNextTick;
            _reassertBrightnessOnNextTick = false;

            if (themeChanged)
            {
                // A theme switch broadcasts a system-wide setting change and the display stack can
                // re-apply the power plan brightness right after it, so write now and once more on
                // the next tick instead of trusting the value that was just set.
                reassertBrightness = true;
                _reassertBrightnessOnNextTick = true;
                ThemeChanged?.Invoke();
            }

            var verifyBrightness = _tickCount % VerifyIntervalTicks == 0;
            if (reassertBrightness
                || _lastAppliedBrightness != brightnessEvaluation.Brightness
                || (verifyBrightness && !IsBrightnessInSync(brightnessEvaluation.Brightness)))
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
        _lastResolvedLocationAtUtc = _settings.LastLocationResolvedAtUtc ?? DateTime.MinValue;
        SettingsStore.Save(_settings);
        SettingsChanged?.Invoke();
    }

    private AutomationStatus CreateInitialStatus() =>
        new(
            Brightness: _lastAppliedBrightness,
            Phase: null,
            NextChange: null,
            ThemeLabel: GetCurrentThemeLabel(),
            ScheduleSource: _settings.UseSunSchedule ? SunScheduleSource : ManualScheduleSource,
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
                ? (_settings.UseSunSchedule ? SunScheduleSource : ManualScheduleSource)
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
            var windowsLocation = await WindowsLocationProvider.TryGetLocationAsync();
            if (windowsLocation != null)
            {
                RememberLocation(windowsLocation);
                return windowsLocation;
            }

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

        // A stored location is only usable while the machine stays in the same time zone: after a
        // move the sun times would be computed for the previous place in the new local time.
        if (_settings.LastLatitude.HasValue &&
            _settings.LastLongitude.HasValue &&
            !HasTimeZoneChanged())
        {
            _lastLocation = new LocationResult(
                _settings.LastLatitude.Value,
                _settings.LastLongitude.Value,
                _settings.LastCity ?? string.Empty,
                _settings.LastCountry ?? string.Empty,
                LastKnownLocationSource);
            _lastResolvedLocationAtUtc = DateTime.UtcNow;
            return _lastLocation;
        }

        if (showMessages)
        {
            ShowMessage(
                owner,
                HasTimeZoneChanged()
                    ? "The time zone changed, so the last known location is stale. Enter a city to calculate sunrise and sunset."
                    : "Location is required to calculate sunrise and sunset. Please enter a city.",
                AppRuntime.AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        return null;
    }

    private bool HasTimeZoneChanged() =>
        !string.IsNullOrEmpty(_settings.LastTimeZoneId) &&
        !string.Equals(_settings.LastTimeZoneId, TimeZoneInfo.Local.Id, StringComparison.OrdinalIgnoreCase);

    private async Task<SunTimes?> GetSunTimesAsync(LocationResult location, bool showMessages, IWin32Window? owner)
    {
        if (_cachedSunTimes != null &&
            _cachedSunDate.Date == DateTime.Today &&
            _cachedLat is { } cachedLat && _cachedLon is { } cachedLon &&
            Math.Abs(cachedLat - location.Latitude) <= CoordinateTolerance &&
            Math.Abs(cachedLon - location.Longitude) <= CoordinateTolerance)
        {
            _usingStaleSunTimes = false;
            return _cachedSunTimes;
        }

        // Back off after a failed request instead of asking again on every tick.
        if (DateTime.UtcNow < _sunTimesRetryAfterUtc)
        {
            _usingStaleSunTimes = _lastKnownSunTimes != null;
            return _lastKnownSunTimes;
        }

        var sunTimes = await SunService.TryGetSunTimesAsync(location.Latitude, location.Longitude, DateTime.Today);
        if (sunTimes != null)
        {
            _cachedSunTimes = sunTimes;
            _cachedSunDate = DateTime.Today;
            _cachedLat = location.Latitude;
            _cachedLon = location.Longitude;
            _lastKnownSunTimes = sunTimes;
            _sunTimesRetryAfterUtc = DateTime.MinValue;
            _usingStaleSunTimes = false;
            return sunTimes;
        }

        _sunTimesRetryAfterUtc = DateTime.UtcNow.Add(SunTimesRetryInterval);
        _usingStaleSunTimes = _lastKnownSunTimes != null;

        if (showMessages && _lastKnownSunTimes == null)
        {
            ShowMessage(
                owner,
                "Unable to get sunrise and sunset from the server. Manual schedule will be used.",
                AppRuntime.AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        // The last successful sun times are far closer to the truth than the manual day/night window,
        // so a single failed request no longer flips the whole schedule.
        return _lastKnownSunTimes;
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
        _settings.LastTimeZoneId = TimeZoneInfo.Local.Id;
        _settings.LastLocationResolvedAtUtc = _lastResolvedLocationAtUtc;
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
        _lastKnownSunTimes = null;
        _sunTimesRetryAfterUtc = DateTime.MinValue;
        _usingStaleSunTimes = false;
    }

    internal static bool IsLocationReusable(
        LocationResult? location,
        DateTime resolvedAtUtc,
        DateTime nowUtc,
        string? resolvedTimeZoneId,
        string currentTimeZoneId)
    {
        if (location == null)
        {
            return false;
        }

        // Resolved in another time zone means another trip: the location is not reused even as a
        // fallback, otherwise sunrise would be computed for the wrong local day.
        if (!string.IsNullOrEmpty(resolvedTimeZoneId) &&
            !string.Equals(resolvedTimeZoneId, currentTimeZoneId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // A last known location stays usable much longer than an IP lookup; without this the app
        // would re-resolve the location on every tick once the automatic sources fail.
        var ttl = string.Equals(location.Source, LastKnownLocationSource, StringComparison.Ordinal)
            ? LastKnownLocationTtl
            : LocationRefreshInterval;

        return nowUtc - resolvedAtUtc < ttl;
    }

    private bool CanReuseResolvedLocation() =>
        IsLocationReusable(
            _lastLocation,
            _lastResolvedLocationAtUtc,
            DateTime.UtcNow,
            _settings.LastTimeZoneId,
            TimeZoneInfo.Local.Id);

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
                $"{LastKnownLocationSource} ({DescribeAge(_settings.LastLocationResolvedAtUtc)})");
        }

        return "No location yet. Add a city or allow geolocation to unlock sunrise and sunset mode.";
    }

    private string BuildSunWindowLabel(SunTimes? sunTimes) =>
        sunTimes != null
            ? $"Sunrise {sunTimes.Sunrise:HH:mm}  |  Sunset {sunTimes.Sunset:HH:mm}"
            : $"Manual window {_settings.DayStartTime:hh\\:mm}  |  Night starts {_settings.NightStartTime:hh\\:mm}";

    private static string DescribeAge(DateTime? resolvedAtUtc)
    {
        if (resolvedAtUtc == null)
        {
            return "unknown age";
        }

        var age = DateTime.UtcNow - resolvedAtUtc.Value;
        if (age < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"{age.TotalMinutes:F0} min ago";
        }

        if (age < TimeSpan.FromDays(1))
        {
            return $"{age.TotalHours:F0} h ago";
        }

        return $"{age.TotalDays:F0} d ago";
    }

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
            LastCountry = settings.LastCountry,
            LastTimeZoneId = settings.LastTimeZoneId,
            LastLocationResolvedAtUtc = settings.LastLocationResolvedAtUtc
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
            LastCountry = settings.LastCountry,
            LastTimeZoneId = settings.LastTimeZoneId,
            LastLocationResolvedAtUtc = settings.LastLocationResolvedAtUtc
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

    internal static AppSettings PreserveLastKnownLocation(AppSettings incoming, AppSettings existing)
    {
        incoming.LastLatitude ??= existing.LastLatitude;
        incoming.LastLongitude ??= existing.LastLongitude;
        incoming.LastCity ??= existing.LastCity;
        incoming.LastCountry ??= existing.LastCountry;
        incoming.LastTimeZoneId ??= existing.LastTimeZoneId;
        incoming.LastLocationResolvedAtUtc ??= existing.LastLocationResolvedAtUtc;
        return incoming;
    }

    private static int ClampTransitionMinutes(int value) => Math.Clamp(value, MinTransitionMinutes, MaxTransitionMinutes);

    private static int Clamp(int value) => Math.Clamp(value, 0, 100);

    private static bool IsBrightnessInSync(int expected)
    {
        // Without a readable value there is nothing to compare against, so the app keeps the plain
        // change-based behaviour instead of writing on every tick.
        if (!BrightnessController.TryGetBrightness(out var actual, out _))
        {
            return true;
        }

        return Math.Abs(actual - expected) <= BrightnessTolerancePercent;
    }
}
