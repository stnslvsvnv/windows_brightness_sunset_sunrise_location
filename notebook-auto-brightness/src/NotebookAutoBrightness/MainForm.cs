using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using NotebookAutoBrightness.Ui;

namespace NotebookAutoBrightness;

internal sealed class MainForm : Form
{
    private const int MinTransitionMinutes = 0;
    private const int MaxTransitionMinutes = 40;

    private readonly AutomationController _controller;

    private readonly CheckBox _enabledCheck;
    private readonly CheckBox _sunScheduleCheck;
    private readonly CheckBox _geolocationCheck;
    private readonly CheckBox _autostartCheck;
    private readonly CheckBox _autoThemeCheck;
    private readonly DateTimePicker _dayStartPicker;
    private readonly DateTimePicker _nightStartPicker;
    private readonly TrackBar _dayBrightness;
    private readonly TrackBar _nightBrightness;
    private readonly TrackBar _transitionDuration;
    private readonly TrackBar _themeLeadTime;
    private readonly Label _dayBrightnessValue;
    private readonly Label _nightBrightnessValue;
    private readonly Label _transitionDurationValue;
    private readonly Label _themeLeadTimeValue;
    private readonly TextBox _cityTextBox;
    private readonly Label _locationLabel;
    private readonly Label _sunWindowLabel;
    private readonly Label _statusLabel;
    private readonly Label _statusDetailLabel;
    private readonly Button _applyButton;

    private ThemePalette _palette = ThemeManager.CreatePalette();
    private bool _suppressSave;
    private bool _isApplying;

    public MainForm(AutomationController controller)
    {
        _controller = controller;

        Text = "Notebook Auto Brightness Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(720, 660);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        Icon = AppRuntime.LoadAppIcon();

        _enabledCheck = new CheckBox { Text = "Automation active", AutoSize = true };
        _sunScheduleCheck = new CheckBox { Text = "Follow sunrise and sunset", AutoSize = true };
        _geolocationCheck = new CheckBox { Text = "Resolve location automatically", AutoSize = true };
        _autostartCheck = new CheckBox { Text = "Start with Windows in background", AutoSize = true };
        _autoThemeCheck = new CheckBox { Text = "Switch Windows theme automatically", AutoSize = true };

        _dayStartPicker = CreateTimePicker();
        _nightStartPicker = CreateTimePicker();

        _dayBrightness = CreateSlider(0, 100);
        _nightBrightness = CreateSlider(0, 100);
        _transitionDuration = CreateSlider(MinTransitionMinutes, MaxTransitionMinutes);
        _themeLeadTime = CreateSlider(MinTransitionMinutes, MaxTransitionMinutes);

        _dayBrightnessValue = CreateValueLabel();
        _nightBrightnessValue = CreateValueLabel();
        _transitionDurationValue = CreateValueLabel();
        _themeLeadTimeValue = CreateValueLabel();

        _cityTextBox = new TextBox
        {
            Width = 280,
            PlaceholderText = "Fallback city, for example Berlin"
        };

        _locationLabel = new Label
        {
            AutoSize = false,
            Width = 500,
            Height = 40
        };

        _sunWindowLabel = new Label
        {
            AutoSize = false,
            Width = 500,
            Height = 32
        };

        _statusLabel = new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold)
        };

        _statusDetailLabel = new Label
        {
            AutoSize = false,
            Width = 520,
            Height = 40
        };

        _applyButton = new Button
        {
            Text = "Apply now",
            Width = 128
        };

        Controls.Add(BuildLayout());

        _enabledCheck.CheckedChanged += (_, _) => SaveSettingsFromControls();
        _sunScheduleCheck.CheckedChanged += (_, _) => SaveSettingsFromControls();
        _geolocationCheck.CheckedChanged += (_, _) => SaveSettingsFromControls();
        _autostartCheck.CheckedChanged += (_, _) => SaveSettingsFromControls();
        _autoThemeCheck.CheckedChanged += (_, _) =>
        {
            UpdateThemeLeadState();
            SaveSettingsFromControls();
        };
        _dayStartPicker.ValueChanged += (_, _) => SaveSettingsFromControls();
        _nightStartPicker.ValueChanged += (_, _) => SaveSettingsFromControls();
        _dayBrightness.ValueChanged += (_, _) =>
        {
            UpdateBrightnessLabels();
            SaveSettingsFromControls();
        };
        _nightBrightness.ValueChanged += (_, _) =>
        {
            UpdateBrightnessLabels();
            SaveSettingsFromControls();
        };
        _transitionDuration.ValueChanged += (_, _) =>
        {
            UpdateTransitionLabels();
            SaveSettingsFromControls();
        };
        _themeLeadTime.ValueChanged += (_, _) =>
        {
            UpdateTransitionLabels();
            SaveSettingsFromControls();
        };
        _cityTextBox.TextChanged += (_, _) => SaveSettingsFromControls();
        _applyButton.Click += async (_, _) => await ApplyNowAsync();

        _controller.StatusChanged += HandleStatusChanged;
        _controller.SettingsChanged += HandleSettingsChanged;
        SystemEvents.UserPreferenceChanged += HandleUserPreferenceChanged;

        LoadSettingsIntoControls(_controller.GetSettings());
        ApplyStatus(_controller.CurrentStatus);
        ApplyCurrentTheme();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _controller.StatusChanged -= HandleStatusChanged;
            _controller.SettingsChanged -= HandleSettingsChanged;
            SystemEvents.UserPreferenceChanged -= HandleUserPreferenceChanged;
        }

        base.Dispose(disposing);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ApplyCurrentTheme();
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 7
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var noteLabel = new Label
        {
            AutoSize = false,
            Width = 660,
            Height = 40,
            Text = "Changes save immediately. Close this window to return to the lightweight tray mode."
        };

        root.Controls.Add(noteLabel, 0, 0);
        root.Controls.Add(BuildGeneralGroup(), 0, 1);
        root.Controls.Add(BuildBrightnessGroup(), 0, 2);
        root.Controls.Add(BuildThemeGroup(), 0, 3);
        root.Controls.Add(BuildScheduleGroup(), 0, 4);
        root.Controls.Add(BuildLocationGroup(), 0, 5);
        root.Controls.Add(BuildFooterPanel(), 0, 6);

        return root;
    }

    private Control BuildGeneralGroup()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        panel.Controls.Add(_enabledCheck);
        panel.Controls.Add(_sunScheduleCheck);
        panel.Controls.Add(_geolocationCheck);
        panel.Controls.Add(_autostartCheck);

        return WrapGroup("General", panel);
    }

    private Control BuildBrightnessGroup()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            AutoSize = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label { Text = "Day brightness", AutoSize = true }, 0, 0);
        layout.Controls.Add(_dayBrightness, 1, 0);
        layout.Controls.Add(_dayBrightnessValue, 2, 0);
        layout.Controls.Add(new Label { Text = "Night brightness", AutoSize = true }, 0, 1);
        layout.Controls.Add(_nightBrightness, 1, 1);
        layout.Controls.Add(_nightBrightnessValue, 2, 1);
        layout.Controls.Add(new Label { Text = "Transition duration", AutoSize = true }, 0, 2);
        layout.Controls.Add(_transitionDuration, 1, 2);
        layout.Controls.Add(_transitionDurationValue, 2, 2);

        return WrapGroup("Brightness", layout);
    }

    private Control BuildThemeGroup()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            AutoSize = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        layout.Controls.Add(_autoThemeCheck, 0, 0);
        layout.SetColumnSpan(_autoThemeCheck, 3);
        layout.Controls.Add(new Label { Text = "Theme lead time", AutoSize = true }, 0, 1);
        layout.Controls.Add(_themeLeadTime, 1, 1);
        layout.Controls.Add(_themeLeadTimeValue, 2, 1);

        return WrapGroup("Theme", layout);
    }

    private Control BuildScheduleGroup()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label { Text = "Day starts at", AutoSize = true }, 0, 0);
        layout.Controls.Add(_dayStartPicker, 1, 0);
        layout.Controls.Add(new Label { Text = "Night starts at", AutoSize = true }, 0, 1);
        layout.Controls.Add(_nightStartPicker, 1, 1);
        layout.Controls.Add(new Label { Text = "Current window", AutoSize = true }, 0, 2);
        layout.Controls.Add(_sunWindowLabel, 1, 2);

        return WrapGroup("Schedule", layout);
    }

    private Control BuildLocationGroup()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label { Text = "Fallback city", AutoSize = true }, 0, 0);
        layout.Controls.Add(_cityTextBox, 1, 0);
        layout.Controls.Add(new Label { Text = "Resolved location", AutoSize = true }, 0, 1);
        layout.Controls.Add(_locationLabel, 1, 1);

        return WrapGroup("Location", layout);
    }

    private Control BuildFooterPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight
        };

        panel.Controls.Add(_applyButton);
        panel.Controls.Add(_statusLabel);
        panel.Controls.Add(_statusDetailLabel);
        return panel;
    }

    private void LoadSettingsIntoControls(AppSettings settings)
    {
        _suppressSave = true;
        _enabledCheck.Checked = settings.Enabled;
        _sunScheduleCheck.Checked = settings.UseSunSchedule;
        _geolocationCheck.Checked = settings.UseGeolocation;
        _autostartCheck.Checked = settings.StartWithWindows;
        _autoThemeCheck.Checked = settings.AutoThemeSwitching;
        _dayBrightness.Value = Clamp(settings.DayBrightness);
        _nightBrightness.Value = Clamp(settings.NightBrightness);
        _transitionDuration.Value = ClampTransitionMinutes(settings.TransitionMinutes);
        _themeLeadTime.Value = ClampTransitionMinutes(settings.ThemeSwitchLeadMinutes);
        _dayStartPicker.Value = DateTime.Today.Add(settings.DayStartTime);
        _nightStartPicker.Value = DateTime.Today.Add(settings.NightStartTime);
        _cityTextBox.Text = settings.City ?? string.Empty;
        UpdateBrightnessLabels();
        UpdateTransitionLabels();
        UpdateThemeLeadState();
        _suppressSave = false;
    }

    private void SaveSettingsFromControls()
    {
        if (_suppressSave)
        {
            return;
        }

        _controller.UpdateSettings(new AppSettings
        {
            Enabled = _enabledCheck.Checked,
            UseSunSchedule = _sunScheduleCheck.Checked,
            UseGeolocation = _geolocationCheck.Checked,
            StartWithWindows = _autostartCheck.Checked,
            AutoThemeSwitching = _autoThemeCheck.Checked,
            DayBrightness = _dayBrightness.Value,
            NightBrightness = _nightBrightness.Value,
            TransitionMinutes = _transitionDuration.Value,
            ThemeSwitchLeadMinutes = _themeLeadTime.Value,
            DayStartTime = _dayStartPicker.Value.TimeOfDay,
            NightStartTime = _nightStartPicker.Value.TimeOfDay,
            City = _cityTextBox.Text.Trim()
        });
    }

    private async Task ApplyNowAsync()
    {
        if (_isApplying)
        {
            return;
        }

        _isApplying = true;
        _applyButton.Enabled = false;

        try
        {
            await _controller.ApplyScheduleAsync(true, this);
        }
        finally
        {
            _applyButton.Enabled = true;
            _isApplying = false;
        }
    }

    private void HandleStatusChanged(AutomationStatus status)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => ApplyStatus(status)));
            return;
        }

        ApplyStatus(status);
    }

    private void HandleSettingsChanged()
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => LoadSettingsIntoControls(_controller.GetSettings())));
            return;
        }

        LoadSettingsIntoControls(_controller.GetSettings());
    }

    private void ApplyStatus(AutomationStatus status)
    {
        _locationLabel.Text = status.LocationLabel;
        _sunWindowLabel.Text = status.SunWindowLabel;
        _statusLabel.Text = status.StatusSummary;
        _statusDetailLabel.Text = status.StatusDetail;
    }

    private void ApplyCurrentTheme()
    {
        _palette = ThemeManager.CreatePalette();
        ThemeManager.ApplyTheme(this, _palette);

        _statusDetailLabel.ForeColor = _palette.TextSecondary;
        _sunWindowLabel.ForeColor = _palette.TextSecondary;
        _locationLabel.ForeColor = _palette.TextSecondary;
        Invalidate(true);
    }

    private void HandleUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!ThemeManager.IsThemeRelatedChange(e.Category) || IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(ApplyCurrentTheme));
            return;
        }

        ApplyCurrentTheme();
    }

    private void UpdateBrightnessLabels()
    {
        _dayBrightnessValue.Text = $"{_dayBrightness.Value}%";
        _nightBrightnessValue.Text = $"{_nightBrightness.Value}%";
    }

    private void UpdateTransitionLabels()
    {
        _transitionDurationValue.Text = FormatMinutes(_transitionDuration.Value);
        _themeLeadTimeValue.Text = FormatThemeLead(_themeLeadTime.Value);
    }

    private void UpdateThemeLeadState()
    {
        _themeLeadTime.Enabled = _autoThemeCheck.Checked;
    }

    private static GroupBox WrapGroup(string text, Control control)
    {
        var group = new GroupBox
        {
            Text = text,
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(10)
        };
        control.Dock = DockStyle.Fill;
        group.Controls.Add(control);
        return group;
    }

    private static DateTimePicker CreateTimePicker() =>
        new()
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "HH:mm",
            ShowUpDown = true,
            Width = 100
        };

    private static TrackBar CreateSlider(int minimum, int maximum) =>
        new()
        {
            Minimum = minimum,
            Maximum = maximum,
            TickFrequency = 5,
            SmallChange = 1,
            LargeChange = 1,
            Width = 340
        };

    private static Label CreateValueLabel() =>
        new()
        {
            AutoSize = true,
            Width = 100
        };

    private static string FormatMinutes(int value) =>
        value == 0 ? "Instant" : $"{value} min";

    private static string FormatThemeLead(int value) =>
        value == 0 ? "At sun shift" : $"{value} min early";

    private static int ClampTransitionMinutes(int value) => Math.Clamp(value, MinTransitionMinutes, MaxTransitionMinutes);

    private static int Clamp(int value) => Math.Clamp(value, 0, 100);
}
