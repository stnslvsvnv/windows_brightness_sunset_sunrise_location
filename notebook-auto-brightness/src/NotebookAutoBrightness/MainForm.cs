using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

namespace NotebookAutoBrightness;

public sealed class MainForm : Form
{
    private const string AppName = "Notebook sunrise/sunset auto brightness";
    private const string AutoRunValueName = "NotebookSunriseSunsetAutoBrightness";
    private const int MinTransitionMinutes = 0;
    private const int MaxTransitionMinutes = 20;

    private readonly NotifyIcon _trayIcon;
    private readonly Timer _timer;

    private AppSettings _settings = new();
    private bool _allowClose;
    private bool _suppressSave;
    private bool _isApplying;
    private int? _lastAppliedBrightness;

    private LocationResult? _lastLocation;
    private SunTimes? _cachedSunTimes;
    private DateTime _cachedSunDate = DateTime.MinValue;
    private double? _cachedLat;
    private double? _cachedLon;

    private readonly CheckBox _enabledCheck;
    private readonly CheckBox _geolocationCheck;
    private readonly CheckBox _sunScheduleCheck;
    private readonly CheckBox _autostartCheck;
    private readonly DateTimePicker _dayStartPicker;
    private readonly DateTimePicker _nightStartPicker;
    private readonly TrackBar _dayBrightness;
    private readonly TrackBar _nightBrightness;
    private readonly TrackBar _transitionDuration;
    private readonly Label _dayBrightnessValue;
    private readonly Label _nightBrightnessValue;
    private readonly Label _transitionDurationValue;
    private readonly TextBox _cityTextBox;
    private readonly Label _locationLabel;
    private readonly Label _statusLabel;
    private readonly Button _applyButton;

    public MainForm()
    {
        Text = AppName;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(640, 520);

        var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        if (File.Exists(iconPath))
        {
            try
            {
                Icon = new Icon(iconPath);
            }
            catch
            {
                Icon = SystemIcons.Application;
            }
        }
        else
        {
            Icon = SystemIcons.Application;
        }

        _enabledCheck = new CheckBox { Text = "Enabled", AutoSize = true };
        _geolocationCheck = new CheckBox { Text = "Use geolocation (IP-based)", AutoSize = true };
        _sunScheduleCheck = new CheckBox { Text = "Use sunrise/sunset schedule", AutoSize = true };
        _autostartCheck = new CheckBox { Text = "Start with Windows", AutoSize = true };

        _dayStartPicker = new DateTimePicker
        {
            Format = DateTimePickerFormat.Time,
            ShowUpDown = true,
            Width = 120
        };
        _nightStartPicker = new DateTimePicker
        {
            Format = DateTimePickerFormat.Time,
            ShowUpDown = true,
            Width = 120
        };

        _dayBrightness = new TrackBar
        {
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            Width = 320
        };
        _nightBrightness = new TrackBar
        {
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            Width = 320
        };
        _transitionDuration = new TrackBar
        {
            Minimum = MinTransitionMinutes,
            Maximum = MaxTransitionMinutes,
            TickFrequency = 1,
            SmallChange = 1,
            LargeChange = 1,
            Width = 320
        };

        _dayBrightnessValue = new Label { AutoSize = true };
        _nightBrightnessValue = new Label { AutoSize = true };
        _transitionDurationValue = new Label { AutoSize = true };

        _cityTextBox = new TextBox { Width = 240 };
        _locationLabel = new Label { AutoSize = true };
        _statusLabel = new Label { AutoSize = true };
        _applyButton = new Button { Text = "Apply now", Width = 120 };

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 5,
            AutoSize = true
        };

        var generalGroup = new GroupBox { Text = "General", Dock = DockStyle.Top, AutoSize = true };
        var generalPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown
        };
        generalPanel.Controls.AddRange(new Control[]
        {
            _enabledCheck,
            _sunScheduleCheck,
            _geolocationCheck,
            _autostartCheck
        });
        generalGroup.Controls.Add(generalPanel);

        var scheduleGroup = new GroupBox { Text = "Manual schedule (used when sunrise/sunset is off or unavailable)", Dock = DockStyle.Top, AutoSize = true };
        var scheduleLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            AutoSize = true
        };
        scheduleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        scheduleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        scheduleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        scheduleLayout.Controls.Add(new Label { Text = "Day starts at", AutoSize = true }, 0, 0);
        scheduleLayout.Controls.Add(_dayStartPicker, 1, 0);
        scheduleLayout.Controls.Add(new Label { Text = "Night starts at", AutoSize = true }, 0, 1);
        scheduleLayout.Controls.Add(_nightStartPicker, 1, 1);
        scheduleGroup.Controls.Add(scheduleLayout);

        var brightnessGroup = new GroupBox { Text = "Brightness", Dock = DockStyle.Top, AutoSize = true };
        var brightnessLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            AutoSize = true
        };
        brightnessLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        brightnessLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        brightnessLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        brightnessLayout.Controls.Add(new Label { Text = "Day brightness", AutoSize = true }, 0, 0);
        brightnessLayout.Controls.Add(_dayBrightness, 1, 0);
        brightnessLayout.Controls.Add(_dayBrightnessValue, 2, 0);
        brightnessLayout.Controls.Add(new Label { Text = "Night brightness", AutoSize = true }, 0, 1);
        brightnessLayout.Controls.Add(_nightBrightness, 1, 1);
        brightnessLayout.Controls.Add(_nightBrightnessValue, 2, 1);
        brightnessLayout.Controls.Add(new Label { Text = "Transition duration", AutoSize = true }, 0, 2);
        brightnessLayout.Controls.Add(_transitionDuration, 1, 2);
        brightnessLayout.Controls.Add(_transitionDurationValue, 2, 2);
        brightnessGroup.Controls.Add(brightnessLayout);

        var locationGroup = new GroupBox { Text = "Location", Dock = DockStyle.Top, AutoSize = true };
        var locationLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true
        };
        locationLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        locationLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        locationLayout.Controls.Add(new Label { Text = "City (used if geolocation is off or unavailable)", AutoSize = true }, 0, 0);
        locationLayout.Controls.Add(_cityTextBox, 1, 0);
        locationLayout.Controls.Add(new Label { Text = "Last known location", AutoSize = true }, 0, 1);
        locationLayout.Controls.Add(_locationLabel, 1, 1);
        locationGroup.Controls.Add(locationLayout);

        var footerPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight
        };
        footerPanel.Controls.Add(_applyButton);
        footerPanel.Controls.Add(_statusLabel);

        mainLayout.Controls.Add(generalGroup);
        mainLayout.Controls.Add(scheduleGroup);
        mainLayout.Controls.Add(brightnessGroup);
        mainLayout.Controls.Add(locationGroup);
        mainLayout.Controls.Add(footerPanel);

        Controls.Add(mainLayout);

        _trayIcon = new NotifyIcon
        {
            Icon = Icon ?? SystemIcons.Application,
            Text = AppName,
            Visible = true
        };
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Open", null, (_, _) => ShowMainWindow());
        trayMenu.Items.Add("Enable/Disable", null, (_, _) => ToggleEnabled());
        trayMenu.Items.Add("Exit", null, (_, _) => ExitApplication());
        _trayIcon.ContextMenuStrip = trayMenu;
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();

        _timer = new Timer { Interval = 60_000 };
        _timer.Tick += async (_, _) => await ApplyScheduleAsync(false);

        _enabledCheck.CheckedChanged += (_, _) => OnSettingsChanged();
        _geolocationCheck.CheckedChanged += (_, _) => OnSettingsChanged();
        _sunScheduleCheck.CheckedChanged += (_, _) => OnSettingsChanged();
        _autostartCheck.CheckedChanged += (_, _) => OnSettingsChanged();
        _dayStartPicker.ValueChanged += (_, _) => OnSettingsChanged();
        _nightStartPicker.ValueChanged += (_, _) => OnSettingsChanged();
        _dayBrightness.ValueChanged += (_, _) => OnBrightnessChanged();
        _nightBrightness.ValueChanged += (_, _) => OnBrightnessChanged();
        _transitionDuration.ValueChanged += (_, _) => OnTransitionDurationChanged();
        _cityTextBox.TextChanged += (_, _) => OnSettingsChanged();
        _applyButton.Click += async (_, _) => await ApplyScheduleAsync(true);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        LoadSettings();
        UpdateControlsFromSettings();
        await ApplyScheduleAsync(true);
        _timer.Start();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized)
        {
            Hide();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnFormClosing(e);
    }

    private void LoadSettings()
    {
        _settings = SettingsStore.Load();
        _settings.StartWithWindows = IsAutoStartEnabled();
    }

    private void UpdateControlsFromSettings()
    {
        _suppressSave = true;
        _enabledCheck.Checked = _settings.Enabled;
        _geolocationCheck.Checked = _settings.UseGeolocation;
        _sunScheduleCheck.Checked = _settings.UseSunSchedule;
        _autostartCheck.Checked = _settings.StartWithWindows;
        _dayBrightness.Value = Clamp(_settings.DayBrightness);
        _nightBrightness.Value = Clamp(_settings.NightBrightness);
        _transitionDuration.Value = ClampTransitionMinutes(_settings.TransitionMinutes);
        _dayStartPicker.Value = DateTime.Today.Add(_settings.DayStartTime);
        _nightStartPicker.Value = DateTime.Today.Add(_settings.NightStartTime);
        _cityTextBox.Text = _settings.City ?? string.Empty;
        UpdateBrightnessLabels();
        UpdateTransitionDurationLabel();
        UpdateLocationLabel();
        _suppressSave = false;
    }

    private void OnBrightnessChanged()
    {
        UpdateBrightnessLabels();
        OnSettingsChanged();

        ApplyImmediateBrightnessPreview();
    }

    private void OnTransitionDurationChanged()
    {
        UpdateTransitionDurationLabel();
        OnSettingsChanged();
        ApplyImmediateBrightnessPreview();
    }

    private void UpdateBrightnessLabels()
    {
        _dayBrightnessValue.Text = $"{_dayBrightness.Value}%";
        _nightBrightnessValue.Text = $"{_nightBrightness.Value}%";
    }

    private void UpdateTransitionDurationLabel()
    {
        _transitionDurationValue.Text = _transitionDuration.Value == 0
            ? "Immediately"
            : $"{_transitionDuration.Value} min";
    }

    private void ApplyImmediateBrightnessPreview()
    {
        if (!_settings.Enabled)
        {
            return;
        }

        var now = DateTime.Now;
        var transitionDuration = TimeSpan.FromMinutes(ClampTransitionMinutes(_transitionDuration.Value));
        var isDay = IsCurrentlyDaytime(now);
        var targetBrightness = isDay ? _dayBrightness.Value : _nightBrightness.Value;

        if (transitionDuration > TimeSpan.Zero)
        {
            if (_settings.UseSunSchedule && _cachedSunTimes != null)
            {
                if (TryGetSunTransitionBrightness(
                        now,
                        _cachedSunTimes,
                        transitionDuration,
                        _dayBrightness.Value,
                        _nightBrightness.Value,
                        out var sunTransitionBrightness,
                        out _,
                        out _))
                {
                    targetBrightness = sunTransitionBrightness;
                }
            }
            else if (TryGetManualTransitionBrightness(
                now,
                _settings.DayStartTime,
                _settings.NightStartTime,
                transitionDuration,
                _dayBrightness.Value,
                _nightBrightness.Value,
                out var manualTransitionBrightness,
                out _,
                out _))
            {
                targetBrightness = manualTransitionBrightness;
            }
        }

        BrightnessController.TrySetBrightness(targetBrightness, out _);
    }

    private void OnSettingsChanged()
    {
        if (_suppressSave)
        {
            return;
        }

        _settings.Enabled = _enabledCheck.Checked;
        _settings.UseGeolocation = _geolocationCheck.Checked;
        _settings.UseSunSchedule = _sunScheduleCheck.Checked;
        _settings.StartWithWindows = _autostartCheck.Checked;
        _settings.DayBrightness = _dayBrightness.Value;
        _settings.NightBrightness = _nightBrightness.Value;
        _settings.TransitionMinutes = ClampTransitionMinutes(_transitionDuration.Value);
        _settings.DayStartTime = _dayStartPicker.Value.TimeOfDay;
        _settings.NightStartTime = _nightStartPicker.Value.TimeOfDay;
        _settings.City = _cityTextBox.Text.Trim();

        if (_settings.StartWithWindows)
        {
            EnableAutoStart();
        }
        else
        {
            DisableAutoStart();
        }

        SettingsStore.Save(_settings);
    }

    private async Task ApplyScheduleAsync(bool showMessages)
    {
        if (_isApplying)
        {
            return;
        }
        _isApplying = true;
        try
        {
            if (!_settings.Enabled)
            {
                _statusLabel.Text = "Status: Disabled";
                return;
            }
            var now = DateTime.Now;
            SunTimes? sunTimes = null;
            string scheduleSource = "Manual schedule";
            if (_settings.UseSunSchedule)
            {
                var location = await ResolveLocationAsync(showMessages);
                if (location != null)
                {
                    sunTimes = await GetSunTimesAsync(location, showMessages);
                    scheduleSource = sunTimes == null ? "Manual schedule (sunrise/sunset unavailable)" : "Sunrise/sunset";
                }
                else
                {
                    scheduleSource = "Manual schedule (location required)";
                }
            }
            var (isDay, nextChange) = sunTimes != null
                ? GetPeriodFromSunTimes(now, sunTimes)
                : GetPeriodFromManualTimes(now, _settings.DayStartTime, _settings.NightStartTime);
            var targetBrightness = isDay ? _settings.DayBrightness : _settings.NightBrightness;
            var periodLabel = isDay ? "Day" : "Night";
            var transitionDuration = TimeSpan.FromMinutes(ClampTransitionMinutes(_settings.TransitionMinutes));

            if (transitionDuration > TimeSpan.Zero)
            {
                if (sunTimes != null &&
                    TryGetSunTransitionBrightness(
                        now,
                        sunTimes,
                        transitionDuration,
                        _settings.DayBrightness,
                        _settings.NightBrightness,
                        out var sunTransitionBrightness,
                        out var sunTransitionLabel,
                        out var sunTransitionEnd))
                {
                    targetBrightness = sunTransitionBrightness;
                    periodLabel = sunTransitionLabel;
                    nextChange = sunTransitionEnd;
                }
                else if (sunTimes == null && TryGetManualTransitionBrightness(
                    now,
                    _settings.DayStartTime,
                    _settings.NightStartTime,
                    transitionDuration,
                    _settings.DayBrightness,
                    _settings.NightBrightness,
                    out var manualTransitionBrightness,
                    out var manualTransitionLabel,
                    out var manualTransitionEnd))
                {
                    targetBrightness = manualTransitionBrightness;
                    periodLabel = manualTransitionLabel;
                    nextChange = manualTransitionEnd;
                }
            }
            if (_lastAppliedBrightness != targetBrightness)
            {
                if (!BrightnessController.TrySetBrightness(targetBrightness, out var error))
                {
                    if (showMessages)
                    {
                        MessageBox.Show(
                            this,
                            $"Failed to set brightness. {error}",
                            AppName,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                    _statusLabel.Text = "Status: Failed to set brightness";
                    return;
                }
                _lastAppliedBrightness = targetBrightness;
            }
            _statusLabel.Text = $"Status: {periodLabel} | {scheduleSource} | Next change: {nextChange:HH:mm}";
        }
        finally
        {
            _isApplying = false;
        }
    }

    private async Task<LocationResult?> ResolveLocationAsync(bool showMessages)
    {
        if (_settings.UseGeolocation)
        {
            var ipLocation = await GeoService.TryGetIpLocationAsync();
            if (ipLocation != null)
            {
                _lastLocation = ipLocation;
                SaveLastLocation(ipLocation);
                UpdateLocationLabel();
                return ipLocation;
            }
        }

        if (_settings.LastLatitude.HasValue && _settings.LastLongitude.HasValue)
        {
            var cached = new LocationResult(
                _settings.LastLatitude.Value,
                _settings.LastLongitude.Value,
                _settings.LastCity ?? string.Empty,
                _settings.LastCountry ?? string.Empty,
                "Last known");
            _lastLocation = cached;
            UpdateLocationLabel();
            return cached;
        }

        if (!string.IsNullOrWhiteSpace(_settings.City))
        {
            var cityLocation = await GeoService.TryGeocodeCityAsync(_settings.City);
            if (cityLocation != null)
            {
                _lastLocation = cityLocation;
                SaveLastLocation(cityLocation);
                UpdateLocationLabel();
                return cityLocation;
            }
        }

        if (showMessages)
        {
            MessageBox.Show(
                this,
                "Location is required to calculate sunrise and sunset. Please enter a city.",
                AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        return null;
    }

    private async Task<SunTimes?> GetSunTimesAsync(LocationResult location, bool showMessages)
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
            MessageBox.Show(
                this,
                "Unable to get sunrise/sunset from the server. Manual schedule will be used.",
                AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        return null;
    }

    private static (bool IsDay, DateTime NextChange) GetPeriodFromSunTimes(DateTime now, SunTimes sunTimes)
    {
        if (now >= sunTimes.Sunrise && now < sunTimes.Sunset)
        {
            return (true, sunTimes.Sunset);
        }

        var next = sunTimes.Sunrise;
        if (now >= sunTimes.Sunset)
        {
            next = sunTimes.Sunrise.AddDays(1);
        }

        return (false, next);
    }

    private static (bool IsDay, DateTime NextChange) GetPeriodFromManualTimes(DateTime now, TimeSpan dayStart, TimeSpan nightStart)
    {
        var todayDay = now.Date.Add(dayStart);
        var todayNight = now.Date.Add(nightStart);

        if (dayStart < nightStart)
        {
            if (now < todayDay)
            {
                return (false, todayDay);
            }

            if (now < todayNight)
            {
                return (true, todayNight);
            }

            return (false, todayDay.AddDays(1));
        }

        if (now >= todayDay)
        {
            return (true, todayNight.AddDays(1));
        }

        if (now < todayNight)
        {
            return (true, todayNight);
        }

        return (false, todayDay);
    }

    private void SaveLastLocation(LocationResult location)
    {
        _settings.LastLatitude = location.Latitude;
        _settings.LastLongitude = location.Longitude;
        _settings.LastCity = location.City;
        _settings.LastCountry = location.Country;
        SettingsStore.Save(_settings);
    }

    private void UpdateLocationLabel()
    {
        if (_lastLocation != null)
        {
            _locationLabel.Text = $"{_lastLocation.City} {_lastLocation.Country} [{_lastLocation.Latitude:F4}, {_lastLocation.Longitude:F4}]";
        }
        else if (_settings.LastLatitude.HasValue && _settings.LastLongitude.HasValue)
        {
            _locationLabel.Text = $"{_settings.LastCity} {_settings.LastCountry} [{_settings.LastLatitude:F4}, {_settings.LastLongitude:F4}]";
        }
        else
        {
            _locationLabel.Text = "Not set";
        }
    }

    private void ShowMainWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ToggleEnabled()
    {
        _enabledCheck.Checked = !_enabledCheck.Checked;
        _settings.Enabled = _enabledCheck.Checked;
        SettingsStore.Save(_settings);
    }

    private void ExitApplication()
    {
        _allowClose = true;
        _trayIcon.Visible = false;
        Close();
    }

    private bool IsCurrentlyDaytime(DateTime now)
    {
        if (_settings.UseSunSchedule && _lastLocation != null)
        {
            var sunTimes = GetSunTimesAsync(_lastLocation, false).Result;
            if (sunTimes != null)
            {
                var (isDay, _) = GetPeriodFromSunTimes(now, sunTimes);
                return isDay;
            }
        }
        
        var (isDayManual, _) = GetPeriodFromManualTimes(now, _settings.DayStartTime, _settings.NightStartTime);
        return isDayManual;
    }

    private static bool TryGetSunTransitionBrightness(
        DateTime now,
        SunTimes sunTimes,
        TimeSpan transitionDuration,
        int dayBrightness,
        int nightBrightness,
        out int brightness,
        out string transitionLabel,
        out DateTime transitionEnd)
    {
        for (var dayOffset = -1; dayOffset <= 1; dayOffset++)
        {
            var sunrise = sunTimes.Sunrise.AddDays(dayOffset);
            if (TryGetTransitionBrightness(
                    now,
                    sunrise - transitionDuration,
                    transitionDuration,
                    nightBrightness,
                    dayBrightness,
                    out brightness))
            {
                transitionLabel = "Dawn";
                transitionEnd = sunrise;
                return true;
            }

            var sunset = sunTimes.Sunset.AddDays(dayOffset);
            if (TryGetTransitionBrightness(
                    now,
                    sunset,
                    transitionDuration,
                    dayBrightness,
                    nightBrightness,
                    out brightness))
            {
                transitionLabel = "Twilight";
                transitionEnd = sunset.Add(transitionDuration);
                return true;
            }
        }

        brightness = 0;
        transitionLabel = string.Empty;
        transitionEnd = DateTime.MinValue;
        return false;
    }

    private static bool TryGetManualTransitionBrightness(
        DateTime now,
        TimeSpan dayStartTime,
        TimeSpan nightStartTime,
        TimeSpan transitionDuration,
        int dayBrightness,
        int nightBrightness,
        out int brightness,
        out string transitionLabel,
        out DateTime transitionEnd)
    {
        for (var dayOffset = -1; dayOffset <= 1; dayOffset++)
        {
            var dayStart = now.Date.AddDays(dayOffset).Add(dayStartTime);
            if (TryGetTransitionBrightness(
                    now,
                    dayStart - transitionDuration,
                    transitionDuration,
                    nightBrightness,
                    dayBrightness,
                    out brightness))
            {
                transitionLabel = "Dawn";
                transitionEnd = dayStart;
                return true;
            }

            var nightStart = now.Date.AddDays(dayOffset).Add(nightStartTime);
            if (TryGetTransitionBrightness(
                    now,
                    nightStart,
                    transitionDuration,
                    dayBrightness,
                    nightBrightness,
                    out brightness))
            {
                transitionLabel = "Twilight";
                transitionEnd = nightStart.Add(transitionDuration);
                return true;
            }
        }

        brightness = 0;
        transitionLabel = string.Empty;
        transitionEnd = DateTime.MinValue;
        return false;
    }

    private static bool TryGetTransitionBrightness(
        DateTime now,
        DateTime transitionStart,
        TimeSpan transitionDuration,
        int fromBrightness,
        int toBrightness,
        out int brightness)
    {
        if (transitionDuration <= TimeSpan.Zero)
        {
            brightness = 0;
            return false;
        }

        var transitionEnd = transitionStart.Add(transitionDuration);
        if (now < transitionStart || now >= transitionEnd)
        {
            brightness = 0;
            return false;
        }

        var progress = (now - transitionStart).TotalMilliseconds / transitionDuration.TotalMilliseconds;
        brightness = InterpolateBrightness(fromBrightness, toBrightness, progress);
        return true;
    }

    private static int InterpolateBrightness(int fromBrightness, int toBrightness, double progress)
    {
        var clampedProgress = Math.Max(0d, Math.Min(1d, progress));
        var value = fromBrightness + ((toBrightness - fromBrightness) * clampedProgress);
        return Clamp((int)Math.Round(value));
    }

    private static int ClampTransitionMinutes(int value) => Math.Min(MaxTransitionMinutes, Math.Max(MinTransitionMinutes, value));

    private static int Clamp(int value) => Math.Min(100, Math.Max(0, value));

    private static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
        return key?.GetValue(AutoRunValueName) != null;
    }

    private static void EnableAutoStart()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        key?.SetValue(AutoRunValueName, Application.ExecutablePath);
    }

    private static void DisableAutoStart()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        key?.DeleteValue(AutoRunValueName, false);
    }
}


