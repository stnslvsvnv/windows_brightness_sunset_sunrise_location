using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NotebookAutoBrightness;

internal sealed class AutomationController
{
    private const int MinTransitionMinutes = 0;
    private const int MaxTransitionMinutes = 40;
    private const int MinCheckIntervalSeconds = 5;
    private const int MaxCheckIntervalSeconds = 60;
    private const int BrightnessTolerancePercent = 2;
    private const int VerifyIntervalTicks = 4;
    private const double LocationMoveToleranceDegrees = 0.02;
    internal const string LastKnownLocationSource = "Last known";
    internal const string SunScheduleSource = "Sunrise and sunset";
    internal const string ManualScheduleSource = "Manual schedule";
    internal static readonly TimeSpan ApplyLeaseTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan ApplyFreshnessLimit = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan LocationRefreshInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan LastKnownLocationTtl = TimeSpan.FromHours(6);

    private readonly string _executablePath;

    private AppSettings _settings = new();
    private DateTime _applyStartedAtUtc = DateTime.MinValue;
    private DateTime _lastCompletedApplyUtc = DateTime.MinValue;
    private int _applyLease;
    private int? _lastAppliedBrightness;
    private int _tickCount;
    private bool _reassertBrightnessOnNextTick;
    private LocationResult? _lastLocation;
    private DateTime _lastResolvedLocationAtUtc = DateTime.MinValue;
    private bool _locationRefreshInFlight;
    private WindowsThemeController.ThemeMode? _lastRequestedTheme;

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
        // A settings change is an explicit user action: take the schedule's choice over again
        // instead of waiting for the next boundary, and let the host apply it right away.
        _lastRequestedTheme = null;
        _lastCompletedApplyUtc = DateTime.MinValue;
        SettingsChanged?.Invoke();
        PublishStatus(BuildStatusFromCurrentState());
    }

    public void ToggleEnabled()
    {
        var settings = GetSettings();
        settings.Enabled = !settings.Enabled;
        UpdateSettings(settings);
    }

    // Called by the host on resume, unlock, clock and display changes: after lost time the value
    // written before the gap cannot be trusted, and the schedule has to take the theme back over
    // from whoever changed it while we were asleep.
    public void HandleSystemEvent(string reason)
    {
        _lastRequestedTheme = null;
        _lastCompletedApplyUtc = DateTime.MinValue;
        AppLog.Write($"system event: {reason}");
    }

    public async Task<AutomationStatus> ApplyScheduleAsync(bool showMessages, IWin32Window? owner = null, bool forceBrightness = false)
    {
        if (IsApplyLeaseActive(_applyStartedAtUtc, DateTime.UtcNow))
        {
            return CurrentStatus;
        }

        var lease = ++_applyLease;
        _applyStartedAtUtc = DateTime.UtcNow;
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

            var gap = DateTime.UtcNow - _lastCompletedApplyUtc;
            if (IsScheduleStale(_lastCompletedApplyUtc, DateTime.UtcNow))
            {
                // Time was lost: the machine slept, the thread froze or a previous apply wedged.
                // Whatever was written before the gap cannot be trusted, so write unconditionally.
                forceBrightness = true;
                if (_lastCompletedApplyUtc != DateTime.MinValue)
                {
                    AppLog.Write($"schedule gap of {gap.TotalMinutes:F1} min, forcing re-apply");
                }
            }

            var now = DateTime.Now;

            SunTimes? sunTimes = null;
            var scheduleSource = ManualScheduleSource;

            if (_settings.UseSunSchedule)
            {
                // The decision never waits for the network: the coordinates are already known from
                // the previous run and the sun times are computed locally. Refreshing the location
                // happens in the background and is picked up by the next pass.
                if (_lastLocation == null)
                {
                    _lastLocation = await ResolveLocationAsync(showMessages, owner);
                }
                else if (NeedsLocationRefresh())
                {
                    _ = RefreshLocationInBackgroundAsync();
                }

                if (_lastLocation is { } location)
                {
                    sunTimes = SunCalculator.TryGetSunTimes(location.Latitude, location.Longitude, now);
                    scheduleSource = SunScheduleSource;
                }
                else
                {
                    scheduleSource = "Manual schedule (location required)";
                }
            }

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

                // Write when the schedule moved (or the user changed settings, or we just woke up)
                // and not merely because the observed theme differs: otherwise every manual switch
                // by the user or another tool would be reverted on the next tick.
                if (targetTheme != _lastRequestedTheme)
                {
                    themeChanged = WindowsThemeController.EnsureTheme(targetTheme);
                    _lastRequestedTheme = targetTheme;
                }

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
                AppLog.Write($"theme switched to {themeLabel}");
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
                    _lastCompletedApplyUtc = DateTime.UtcNow;
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
            _lastCompletedApplyUtc = DateTime.UtcNow;
            PublishStatus(applied);
            return applied;
        }
        finally
        {
            // Only the owner of the current lease may release it: a wedged apply that times out and
            // later resumes must not clear the lease of a newer one.
            if (_applyLease == lease)
            {
                _applyStartedAtUtc = DateTime.MinValue;
            }
        }
    }

    private void LoadSettings()
    {
        _settings = NormalizeSettings(SettingsStore.Load());
        _settings.StartWithWindows = IsAutoStartEnabled();
        _lastResolvedLocationAtUtc = _settings.LastLocationResolvedAtUtc ?? DateTime.MinValue;
        _lastLocation = SeedLocationFromSettings();
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
            SunWindowLabel: BuildSunWindowLabel(GetCurrentSunTimesPreview()));

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

    private bool NeedsLocationRefresh() =>
        !_locationRefreshInFlight &&
        !IsLocationReusable(
            _lastLocation,
            _lastResolvedLocationAtUtc,
            DateTime.UtcNow,
            _settings.LastTimeZoneId,
            TimeZoneInfo.Local.Id);

    // Coordinates only say *where* to compute the sun times for, so refreshing them is done off the
    // decision path: a slow or unreachable geolocation service must never delay a theme switch.
    private async Task RefreshLocationInBackgroundAsync()
    {
        _locationRefreshInFlight = true;
        try
        {
            var previous = _lastLocation;
            var refreshed = await ResolveLocationAsync(showMessages: false, owner: null);
            if (refreshed == null)
            {
                return;
            }

            _lastLocation = refreshed;
            if (MovedFarEnough(previous, refreshed))
            {
                _lastRequestedTheme = null;
                _reassertBrightnessOnNextTick = true;
                AppLog.Write(
                    $"location moved to {refreshed.Latitude:F4}, {refreshed.Longitude:F4} ({refreshed.Source})");
            }
        }
        catch (Exception ex)
        {
            AppLog.WriteError("location refresh", ex);
        }
        finally
        {
            _locationRefreshInFlight = false;
        }
    }

    private static bool MovedFarEnough(LocationResult? from, LocationResult to) =>
        from == null ||
        Math.Abs(from.Latitude - to.Latitude) > LocationMoveToleranceDegrees ||
        Math.Abs(from.Longitude - to.Longitude) > LocationMoveToleranceDegrees;

    private LocationResult? SeedLocationFromSettings()
    {
        if (_settings.LastLatitude == null || _settings.LastLongitude == null)
        {
            return null;
        }

        return new LocationResult(
            _settings.LastLatitude.Value,
            _settings.LastLongitude.Value,
            _settings.LastCity ?? string.Empty,
            _settings.LastCountry ?? string.Empty,
            LastKnownLocationSource);
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

    private SunTimes? GetCurrentSunTimesPreview()
    {
        if (!_settings.UseSunSchedule || _lastLocation == null)
        {
            return null;
        }

        return SunCalculator.TryGetSunTimes(_lastLocation.Latitude, _lastLocation.Longitude, DateTime.Now);
    }

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
        var apps = WindowsThemeController.GetCurrentTheme();
        var system = WindowsThemeController.GetSystemTheme();
        if (apps != null && system != null && apps != system)
        {
            // Windows mode can be "Custom" (different app and system modes): report both.
            return $"apps {DescribeTheme(apps)}, system {DescribeTheme(system)}";
        }

        return DescribeTheme(apps ?? system);
    }

    private static string DescribeTheme(WindowsThemeController.ThemeMode? mode) =>
        mode switch
        {
            WindowsThemeController.ThemeMode.Dark => "Dark",
            WindowsThemeController.ThemeMode.Light => "Light",
            _ => "System"
        };

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
            CheckIntervalSeconds = ClampCheckIntervalSeconds(settings.CheckIntervalSeconds),
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
            CheckIntervalSeconds = settings.CheckIntervalSeconds,
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

    private static int ClampCheckIntervalSeconds(int value) =>
        Math.Clamp(value, MinCheckIntervalSeconds, MaxCheckIntervalSeconds);

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

    internal static bool IsApplyLeaseActive(DateTime applyStartedAtUtc, DateTime nowUtc) =>
        nowUtc - applyStartedAtUtc < ApplyLeaseTimeout;

    internal static bool IsScheduleStale(DateTime lastCompletedApplyUtc, DateTime nowUtc) =>
        nowUtc - lastCompletedApplyUtc > ApplyFreshnessLimit;
}
