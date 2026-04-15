using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using NotebookAutoBrightness.Ui;
using Timer = System.Windows.Forms.Timer;

namespace NotebookAutoBrightness;

public sealed class MainForm : Form
{
    private const string AppName = "Notebook sunrise/sunset auto brightness";
    private const string AutoRunValueName = "NotebookSunriseSunsetAutoBrightness";
    private const int MinTransitionMinutes = 0;
    private const int MaxTransitionMinutes = 40;
    private static readonly TimeSpan LocationRefreshInterval = TimeSpan.FromMinutes(30);

    private readonly Font _heroTitleFont = new("Segoe UI Semibold", 15.5F, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _heroSubtitleFont = new("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _heroValueFont = new("Segoe UI Semibold", 16.5F, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _heroTagFont = new("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _sectionKickerFont = new("Segoe UI Semibold", 8F, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _sectionTitleFont = new("Segoe UI Semibold", 11.75F, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _sectionSubtitleFont = new("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _metaValueFont = new("Segoe UI Semibold", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _inputCaptionFont = new("Segoe UI Semibold", 8F, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _bodyFont = new("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point);

    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _trayMenu;
    private readonly Timer _timer;
    private readonly SettingToggleRow _enabledToggle;
    private readonly SettingToggleRow _sunScheduleToggle;
    private readonly SettingToggleRow _geolocationToggle;
    private readonly SettingToggleRow _autostartToggle;
    private readonly SettingToggleRow _autoThemeToggle;
    private readonly MetricSlider _dayBrightnessSlider;
    private readonly MetricSlider _nightBrightnessSlider;
    private readonly MetricSlider _transitionSlider;
    private readonly MetricSlider _themeLeadSlider;
    private readonly DateTimePicker _dayStartPicker;
    private readonly DateTimePicker _nightStartPicker;
    private readonly TextBox _cityTextBox;
    private readonly Label _locationLabel;
    private readonly Label _scheduleModeLabel;
    private readonly Label _themeModeSummaryLabel;
    private readonly Label _themePreviewNoteLabel;
    private readonly Label _statusLabel;
    private readonly Label _statusDetailLabel;
    private readonly ThemedButton _applyButton;

    private readonly Label _heroTitleLabel;
    private readonly Label _heroSubtitleLabel;
    private readonly Label _heroBrightnessValueLabel;
    private readonly Label _heroPhaseLabel;
    private readonly Label _heroLocationLabel;
    private readonly Label _heroNextChangeLabel;
    private readonly Label _heroThemeLabel;
    private readonly Label _heroScheduleSourceLabel;
    private readonly Label _heroSunWindowLabel;

    private AppSettings _settings = new();
    private ThemePalette _palette = ThemeManager.CreatePalette();
    private bool _allowClose;
    private bool _suppressSave;
    private bool _isApplying;
    private int? _lastAppliedBrightness;

    private LocationResult? _lastLocation;
    private DateTime _lastResolvedLocationAtUtc = DateTime.MinValue;
    private SunTimes? _cachedSunTimes;
    private DateTime _cachedSunDate = DateTime.MinValue;
    private double? _cachedLat;
    private double? _cachedLon;

    public MainForm()
    {
        Text = AppName;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1080, 640);
        MinimumSize = new Size(980, 600);
        DoubleBuffered = true;

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        Icon = LoadAppIcon();

        _enabledToggle = CreateToggle("Automation active", "Enable brightness and theme automation.");
        _sunScheduleToggle = CreateToggle("Follow sunrise and sunset", "Use solar times when location is known.");
        _geolocationToggle = CreateToggle("Resolve location automatically", "Use IP lookup with cached fallback.");
        _autostartToggle = CreateToggle("Launch on sign-in", "Start in tray after sign-in.");
        _autoThemeToggle = CreateToggle("Switch Windows theme", "Flip Windows mode around day and night.");

        _dayBrightnessSlider = CreateSlider("Day brightness", "Peak daytime target for the notebook panel.", static value => $"{value}%", 0, 100);
        _nightBrightnessSlider = CreateSlider("Night brightness", "Comfortable evening level after sunset.", static value => $"{value}%", 0, 100);
        _transitionSlider = CreateSlider("Smooth sunrise/sunset fade", "Minutes used to blend brightness around dawn and dusk.", FormatTransitionMinutes, MinTransitionMinutes, MaxTransitionMinutes);
        _themeLeadSlider = CreateSlider("Theme lead time", "Let Windows switch a little before the light change.", FormatThemeLeadTime, MinTransitionMinutes, MaxTransitionMinutes);

        _dayStartPicker = CreateTimePicker();
        _nightStartPicker = CreateTimePicker();
        _cityTextBox = new TextBox
        {
            Dock = DockStyle.Top,
            Height = 36,
            Font = _bodyFont,
            PlaceholderText = "Fallback city, for example Berlin or Lisbon"
        };

        _locationLabel = CreateLabel("Waiting for first location lookup.", "BodyPrimary", _bodyFont, autoSize: false);
        _locationLabel.Dock = DockStyle.Top;
        _locationLabel.Height = 18;
        _locationLabel.AutoEllipsis = true;

        _scheduleModeLabel = CreateLabel("Solar schedule is primary. Manual times stay ready as fallback.", "BodySecondary", _bodyFont, autoSize: false);
        _scheduleModeLabel.Dock = DockStyle.Top;
        _scheduleModeLabel.Height = 18;
        _scheduleModeLabel.AutoEllipsis = true;

        _themeModeSummaryLabel = CreateLabel("App and Windows theme stay in sync.", "BodyPrimary", _bodyFont, autoSize: false);
        _themeModeSummaryLabel.Dock = DockStyle.Top;
        _themeModeSummaryLabel.Height = 18;
        _themeModeSummaryLabel.AutoEllipsis = true;

        _themePreviewNoteLabel = CreateLabel("Switching the Windows theme updates this window immediately.", "BodySecondary", _bodyFont, autoSize: false);
        _themePreviewNoteLabel.Dock = DockStyle.Top;
        _themePreviewNoteLabel.Height = 18;
        _themePreviewNoteLabel.AutoEllipsis = true;

        _statusLabel = CreateLabel("Ready.", "FooterStatus", new Font("Segoe UI Semibold", 10F, FontStyle.Regular, GraphicsUnit.Point), autoSize: true);
        _statusDetailLabel = CreateLabel("Minimize the window to keep automation in the tray.", "BodySecondary", _bodyFont, autoSize: false);
        _statusDetailLabel.Dock = DockStyle.Top;
        _statusDetailLabel.Height = 18;
        _statusDetailLabel.AutoEllipsis = true;

        _applyButton = new ThemedButton
        {
            Text = "Apply now",
            VisualKind = ButtonVisualKind.Primary,
            Width = 148
        };

        _heroTitleLabel = CreateLabel("Notebook Auto Brightness", "HeroTitle", _heroTitleFont, autoSize: true);
        _heroSubtitleLabel = CreateLabel("Sunrise-driven brightness and Windows theme sync.", "HeroSubtitle", _heroSubtitleFont, autoSize: true);
        _heroBrightnessValueLabel = CreateLabel("--", "HeroValue", _heroValueFont, autoSize: true);
        _heroPhaseLabel = CreateLabel("READY", "HeroTag", _heroTagFont, autoSize: true);
        _heroLocationLabel = CreateLabel("Waiting for location...", "HeroBody", _bodyFont, autoSize: false);
        _heroLocationLabel.Dock = DockStyle.Top;
        _heroLocationLabel.Height = 18;
        _heroLocationLabel.AutoEllipsis = true;
        _heroNextChangeLabel = CreateLabel("--:--", "MetaValue", _metaValueFont, autoSize: true);
        _heroThemeLabel = CreateLabel("Live sync", "MetaValue", _metaValueFont, autoSize: true);
        _heroScheduleSourceLabel = CreateLabel("Manual fallback", "MetaValue", _metaValueFont, autoSize: true);
        _heroSunWindowLabel = CreateLabel("Manual window 07:00 to 19:00", "HeroBody", _bodyFont, autoSize: false);
        _heroSunWindowLabel.Dock = DockStyle.Top;
        _heroSunWindowLabel.Height = 18;
        _heroSunWindowLabel.AutoEllipsis = true;

        Controls.Add(BuildLayout());

        _trayMenu = new ContextMenuStrip();
        _trayMenu.Items.Add("Open", null, (_, _) => ShowMainWindow());
        _trayMenu.Items.Add("Enable/Disable", null, (_, _) => ToggleEnabled());
        _trayMenu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _trayIcon = new NotifyIcon
        {
            Icon = Icon ?? SystemIcons.Application,
            Text = AppName,
            Visible = true,
            ContextMenuStrip = _trayMenu
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();

        _timer = new Timer { Interval = 15_000 };
        _timer.Tick += async (_, _) => await ApplyScheduleAsync(false);

        _enabledToggle.CheckedChanged += (_, _) => OnSettingsChanged();
        _sunScheduleToggle.CheckedChanged += (_, _) => OnSettingsChanged();
        _geolocationToggle.CheckedChanged += (_, _) => OnSettingsChanged();
        _autostartToggle.CheckedChanged += (_, _) => OnSettingsChanged();
        _autoThemeToggle.CheckedChanged += (_, _) => OnAutoThemeSettingChanged();
        _dayBrightnessSlider.ValueChanged += (_, _) => OnBrightnessChanged();
        _nightBrightnessSlider.ValueChanged += (_, _) => OnBrightnessChanged();
        _transitionSlider.ValueChanged += (_, _) => OnTransitionDurationChanged();
        _themeLeadSlider.ValueChanged += (_, _) => OnThemeSwitchLeadTimeChanged();
        _dayStartPicker.ValueChanged += (_, _) => OnSettingsChanged();
        _nightStartPicker.ValueChanged += (_, _) => OnSettingsChanged();
        _cityTextBox.TextChanged += (_, _) => OnSettingsChanged();
        _applyButton.Click += async (_, _) => await ApplyScheduleAsync(true);

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        UpdateThemeSwitchControlsState();
        UpdateThemeStatusSummary();
        UpdateScheduleModeSummary();
        ApplyCurrentTheme();
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        LoadSettings();
        UpdateControlsFromSettings();
        ApplyCurrentTheme();
        await ApplyScheduleAsync(true);
        _timer.Start();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var backgroundBrush = new LinearGradientBrush(ClientRectangle, _palette.WindowBackground, _palette.WindowBackgroundAlt, 90f);
        e.Graphics.FillRectangle(backgroundBrush, ClientRectangle);
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _trayIcon.Dispose();
            _trayMenu.Dispose();
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }

    private TableLayoutPanel BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 16, 20, 16),
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var header = BuildHeaderStrip();
        header.Margin = new Padding(0, 0, 0, 12);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(BuildContentSurface(), 0, 1);

        return root;
    }

    private Control BuildHeaderStrip()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titleStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        titleStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titleStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titleStack.Controls.Add(_heroTitleLabel, 0, 0);
        titleStack.Controls.Add(_heroSubtitleLabel, 0, 1);

        var metricGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(18, 0, 12, 0)
        };
        for (var i = 0; i < 4; i++)
        {
            metricGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }

        metricGrid.Controls.Add(CreateSummaryMetric("Output", _heroBrightnessValueLabel), 0, 0);
        metricGrid.Controls.Add(CreateSummaryMetric("Phase", _heroPhaseLabel), 1, 0);
        metricGrid.Controls.Add(CreateSummaryMetric("Next change", _heroNextChangeLabel), 2, 0);
        metricGrid.Controls.Add(CreateSummaryMetric("Theme", _heroThemeLabel), 3, 0);

        var infoStrip = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 6, 0, 0)
        };
        infoStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        infoStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        infoStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        infoStrip.Controls.Add(CreateMetaBlock("Schedule", _heroScheduleSourceLabel), 0, 0);
        infoStrip.Controls.Add(CreateMetaBlock("Location", _heroLocationLabel), 1, 0);
        infoStrip.Controls.Add(CreateMetaBlock("Status", _statusDetailLabel), 2, 0);

        layout.Controls.Add(titleStack, 0, 0);
        layout.Controls.Add(metricGrid, 1, 0);
        layout.Controls.Add(_applyButton, 2, 0);
        layout.Controls.Add(infoStrip, 0, 1);
        layout.SetColumnSpan(infoStrip, 3);

        return layout;
    }

    private ThemedCardPanel BuildContentSurface()
    {
        var card = new ThemedCardPanel
        {
            Tone = SurfaceTone.Standard,
            Dock = DockStyle.Fill,
            Padding = new Padding(0)
        };

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31F));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1F));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1F));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31F));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        content.Controls.Add(BuildLeftPane(), 0, 0);
        content.Controls.Add(CreateVerticalDivider(), 1, 0);
        content.Controls.Add(BuildCenterPane(), 2, 0);
        content.Controls.Add(CreateVerticalDivider(), 3, 0);
        content.Controls.Add(BuildRightPane(), 4, 0);

        card.Controls.Add(content);
        return card;
    }

    private Control BuildLeftPane() =>
        CreatePaneStack(
            CreateSectionBlock("Automation", "Core behavior and startup rules.", BuildAutomationContent()));

    private Control BuildCenterPane() =>
        CreatePaneStack(
            CreateSectionBlock("Brightness", "Day, night and transition behavior.", BuildBrightnessContent()),
            CreateSectionBlock("Theme", "Windows theme sync and lead time.", BuildThemeContent()));

    private Control BuildRightPane() =>
        CreatePaneStack(
            CreateSectionBlock("Schedule", "Manual fallback and solar mode.", BuildScheduleContent()),
            CreateSectionBlock("Location", "City override and last resolved position.", BuildLocationContent()));

    private Control BuildAutomationContent()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.Transparent
        };

        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

        _enabledToggle.Dock = DockStyle.Fill;
        _sunScheduleToggle.Dock = DockStyle.Fill;
        _geolocationToggle.Dock = DockStyle.Fill;
        _autostartToggle.Dock = DockStyle.Fill;

        content.Controls.Add(_enabledToggle, 0, 0);
        content.Controls.Add(_sunScheduleToggle, 0, 1);
        content.Controls.Add(_geolocationToggle, 0, 2);
        content.Controls.Add(_autostartToggle, 0, 3);
        return content;
    }

    private Control BuildBrightnessContent()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent
        };

        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));

        _dayBrightnessSlider.Dock = DockStyle.Fill;
        _nightBrightnessSlider.Dock = DockStyle.Fill;
        _transitionSlider.Dock = DockStyle.Fill;

        content.Controls.Add(_dayBrightnessSlider, 0, 0);
        content.Controls.Add(_nightBrightnessSlider, 0, 1);
        content.Controls.Add(_transitionSlider, 0, 2);
        return content;
    }

    private Control BuildScheduleContent()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.Transparent
        };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var pickerRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            BackColor = Color.Transparent
        };
        pickerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        pickerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        pickerRow.Controls.Add(CreateInputBlock("DAY", _dayStartPicker), 0, 0);
        pickerRow.Controls.Add(CreateInputBlock("NIGHT", _nightStartPicker), 1, 0);

        content.Controls.Add(_scheduleModeLabel, 0, 0);
        content.Controls.Add(CreateMetaBlock("CURRENT WINDOW", _heroSunWindowLabel), 0, 1);
        pickerRow.Margin = new Padding(0, 10, 0, 0);
        content.Controls.Add(pickerRow, 0, 2);
        return content;
    }

    private Control BuildLocationContent()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        content.Controls.Add(CreateInputBlock("CITY OVERRIDE", _cityTextBox), 0, 0);
        _locationLabel.Margin = new Padding(0, 10, 0, 0);
        content.Controls.Add(_locationLabel, 0, 1);
        return content;
    }

    private Control BuildThemeContent()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.Transparent
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _autoThemeToggle.Dock = DockStyle.Fill;
        _themeLeadSlider.Dock = DockStyle.Fill;

        content.Controls.Add(_autoThemeToggle, 0, 0);
        content.Controls.Add(_themeLeadSlider, 0, 1);
        content.Controls.Add(_themeModeSummaryLabel, 0, 2);
        return content;
    }

    private Control CreatePaneStack(params Control[] sections)
    {
        var pane = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = sections.Length == 0 ? 1 : sections.Length * 2 - 1,
            BackColor = Color.Transparent,
            Padding = new Padding(18, 18, 18, 18)
        };
        pane.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        for (var i = 0; i < pane.RowCount; i++)
        {
            pane.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        for (var i = 0; i < sections.Length; i++)
        {
            var row = i * 2;
            sections[i].Margin = new Padding(0);
            pane.Controls.Add(sections[i], 0, row);
            if (i < sections.Length - 1)
            {
                var divider = CreateDivider();
                divider.Margin = new Padding(0, 14, 0, 14);
                pane.Controls.Add(divider, 0, row + 1);
            }
        }

        return pane;
    }

    private Control CreateSectionBlock(string title, string subtitle, Control content)
    {
        var block = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        block.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        block.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        block.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        block.Controls.Add(CreateLabel(title, "SectionTitle", _sectionTitleFont, autoSize: true), 0, 0);
        block.Controls.Add(CreateLabel(subtitle, "SectionSubtitle", _sectionSubtitleFont, autoSize: true), 0, 1);
        content.Margin = new Padding(0, 12, 0, 0);
        block.Controls.Add(content, 0, 2);
        return block;
    }

    private Control CreateSummaryMetric(string caption, Label valueLabel)
    {
        var block = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 14, 0)
        };
        block.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        block.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        block.Controls.Add(CreateLabel(caption.ToUpperInvariant(), "HeroMetricCaption", _sectionKickerFont, autoSize: true), 0, 0);
        valueLabel.Margin = new Padding(0, 2, 0, 0);
        block.Controls.Add(valueLabel, 0, 1);
        return block;
    }

    private Control CreateMetaBlock(string caption, Label valueLabel)
    {
        var block = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        block.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        block.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        block.Controls.Add(CreateLabel(caption.ToUpperInvariant(), "HeroMetricCaption", _sectionKickerFont, autoSize: true), 0, 0);
        valueLabel.Margin = new Padding(0, 3, 0, 0);
        block.Controls.Add(valueLabel, 0, 1);
        return block;
    }

    private Control CreateDivider() =>
        new Label
        {
            Tag = "Divider",
            Dock = DockStyle.Top,
            Height = 1,
            Margin = new Padding(0),
            AutoSize = false
        };

    private Control CreateVerticalDivider() =>
        new Label
        {
            Tag = "Divider",
            Dock = DockStyle.Fill,
            Width = 1,
            Margin = new Padding(0),
            AutoSize = false
        };

    private Control CreateInputBlock(string caption, Control input)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 8)
        };
        panel.Controls.Add(CreateLabel(caption, "InputCaption", _inputCaptionFont, autoSize: true), 0, 0);
        panel.Controls.Add(input, 0, 1);
        return panel;
    }

    private void LoadSettings()
    {
        _settings = SettingsStore.Load();
        _settings.StartWithWindows = IsAutoStartEnabled();
    }

    private void UpdateControlsFromSettings()
    {
        _suppressSave = true;
        _enabledToggle.Checked = _settings.Enabled;
        _sunScheduleToggle.Checked = _settings.UseSunSchedule;
        _geolocationToggle.Checked = _settings.UseGeolocation;
        _autostartToggle.Checked = _settings.StartWithWindows;
        _autoThemeToggle.Checked = _settings.AutoThemeSwitching;
        _dayBrightnessSlider.Value = Clamp(_settings.DayBrightness);
        _nightBrightnessSlider.Value = Clamp(_settings.NightBrightness);
        _transitionSlider.Value = ClampTransitionMinutes(_settings.TransitionMinutes);
        _themeLeadSlider.Value = ClampTransitionMinutes(_settings.ThemeSwitchLeadMinutes);
        _dayStartPicker.Value = DateTime.Today.Add(_settings.DayStartTime);
        _nightStartPicker.Value = DateTime.Today.Add(_settings.NightStartTime);
        _cityTextBox.Text = _settings.City ?? string.Empty;
        UpdateThemeSwitchControlsState();
        UpdateThemeStatusSummary();
        UpdateScheduleModeSummary();
        UpdateLocationLabel();
        _suppressSave = false;
    }

    private void OnBrightnessChanged()
    {
        if (_suppressSave)
        {
            return;
        }

        OnSettingsChanged();
        ApplyImmediateBrightnessPreview();
    }

    private void OnTransitionDurationChanged()
    {
        if (_suppressSave)
        {
            return;
        }

        OnSettingsChanged();
        ApplyImmediateBrightnessPreview();
    }

    private void OnThemeSwitchLeadTimeChanged()
    {
        if (_suppressSave)
        {
            return;
        }

        OnSettingsChanged();
        ApplyImmediateThemePreview();
    }

    private void OnAutoThemeSettingChanged()
    {
        if (_suppressSave)
        {
            return;
        }

        UpdateThemeSwitchControlsState();
        UpdateThemeStatusSummary();
        OnSettingsChanged();
        ApplyImmediateThemePreview();
    }

    private void UpdateThemeSwitchControlsState()
    {
        _themeLeadSlider.Enabled = _autoThemeToggle.Checked;
        _themeLeadSlider.Description = _autoThemeToggle.Checked
            ? "Windows theme flips before the light change."
            : "Enable theme switching to adjust this lead time.";
    }

    private void UpdateThemeStatusSummary()
    {
        var currentTheme = WindowsThemeController.GetCurrentTheme();
        var themeLabel = currentTheme == WindowsThemeController.ThemeMode.Dark ? "Dark" : "Light";

        _themeModeSummaryLabel.Text = _autoThemeToggle.Checked
            ? $"Windows and app theme stay synced. Current mode: {themeLabel}."
            : $"App still follows Windows manually. Current mode: {themeLabel}.";
        _themePreviewNoteLabel.Text = _autoThemeToggle.Checked
            ? "This window updates as soon as Windows theme changes."
            : "Enable this to switch Windows automatically near sunrise or sunset.";
    }

    private void UpdateScheduleModeSummary()
    {
        _scheduleModeLabel.Text = _sunScheduleToggle.Checked
            ? "Solar schedule is primary. Manual times stay ready as fallback."
            : "Manual schedule is primary until solar mode is enabled again.";
    }

    private void ApplyImmediateBrightnessPreview()
    {
        if (!_settings.Enabled)
        {
            return;
        }

        var evaluation = ScheduleCalculator.EvaluateBrightness(
            DateTime.Now,
            GetCachedSunTimesForPreview(),
            _settings.DayStartTime,
            _settings.NightStartTime,
            _dayBrightnessSlider.Value,
            _nightBrightnessSlider.Value,
            TimeSpan.FromMinutes(ClampTransitionMinutes(_transitionSlider.Value)));

        BrightnessController.TrySetBrightness(evaluation.Brightness, out _);
        _heroBrightnessValueLabel.Text = $"{evaluation.Brightness}%";
        _heroPhaseLabel.Text = ScheduleCalculator.GetPhaseLabel(evaluation.Phase).ToUpperInvariant();
    }

    private void ApplyImmediateThemePreview()
    {
        if (!_settings.Enabled || !_autoThemeToggle.Checked)
        {
            UpdateThemeStatusSummary();
            return;
        }

        var useLightTheme = ScheduleCalculator.ShouldUseLightTheme(
            DateTime.Now,
            GetCachedSunTimesForPreview(),
            _settings.DayStartTime,
            _settings.NightStartTime,
            TimeSpan.FromMinutes(ClampTransitionMinutes(_themeLeadSlider.Value)));

        var targetTheme = useLightTheme ? WindowsThemeController.ThemeMode.Light : WindowsThemeController.ThemeMode.Dark;
        if (WindowsThemeController.GetCurrentTheme() != targetTheme)
        {
            _ = WindowsThemeController.SetTheme(targetTheme);
        }

        ApplyCurrentTheme();
    }

    private void OnSettingsChanged()
    {
        if (_suppressSave)
        {
            return;
        }

        var previousUseGeolocation = _settings.UseGeolocation;
        var previousCity = _settings.City ?? string.Empty;

        _settings.Enabled = _enabledToggle.Checked;
        _settings.UseSunSchedule = _sunScheduleToggle.Checked;
        _settings.UseGeolocation = _geolocationToggle.Checked;
        _settings.StartWithWindows = _autostartToggle.Checked;
        _settings.AutoThemeSwitching = _autoThemeToggle.Checked;
        _settings.DayBrightness = _dayBrightnessSlider.Value;
        _settings.NightBrightness = _nightBrightnessSlider.Value;
        _settings.TransitionMinutes = ClampTransitionMinutes(_transitionSlider.Value);
        _settings.ThemeSwitchLeadMinutes = ClampTransitionMinutes(_themeLeadSlider.Value);
        _settings.DayStartTime = _dayStartPicker.Value.TimeOfDay;
        _settings.NightStartTime = _nightStartPicker.Value.TimeOfDay;
        _settings.City = _cityTextBox.Text.Trim();

        UpdateThemeSwitchControlsState();
        UpdateThemeStatusSummary();
        UpdateScheduleModeSummary();

        if (previousUseGeolocation != _settings.UseGeolocation ||
            !string.Equals(previousCity, _settings.City, StringComparison.Ordinal))
        {
            InvalidateLocationCache();
        }

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
                _heroBrightnessValueLabel.Text = "--";
                _heroPhaseLabel.Text = "PAUSED";
                _heroThemeLabel.Text = "Live sync";
                _heroScheduleSourceLabel.Text = "Automation off";
                _heroSunWindowLabel.Text = $"Manual window {_settings.DayStartTime:hh\\:mm} to {_settings.NightStartTime:hh\\:mm}";
                SetStatus(
                    "Automation paused.",
                    "Brightness and theme changes are off until you enable the master switch.");
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

            string? themeLabel;
            if (_settings.AutoThemeSwitching)
            {
                var useLightTheme = ScheduleCalculator.ShouldUseLightTheme(
                    now,
                    sunTimes,
                    _settings.DayStartTime,
                    _settings.NightStartTime,
                    TimeSpan.FromMinutes(ClampTransitionMinutes(_settings.ThemeSwitchLeadMinutes)));
                var targetTheme = useLightTheme ? WindowsThemeController.ThemeMode.Light : WindowsThemeController.ThemeMode.Dark;
                themeLabel = useLightTheme ? "Light" : "Dark";
                if (WindowsThemeController.GetCurrentTheme() != targetTheme && WindowsThemeController.SetTheme(targetTheme))
                {
                    ApplyCurrentTheme();
                }
            }
            else
            {
                var currentTheme = WindowsThemeController.GetCurrentTheme();
                themeLabel = currentTheme == WindowsThemeController.ThemeMode.Dark ? "Dark" : "Light";
            }

            if (_lastAppliedBrightness != brightnessEvaluation.Brightness)
            {
                if (!BrightnessController.TrySetBrightness(brightnessEvaluation.Brightness, out var error))
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

                    SetStatus("Brightness update failed.", "Your display driver or hardware rejected the change.");
                    return;
                }

                _lastAppliedBrightness = brightnessEvaluation.Brightness;
            }

            UpdateHeroSummary(brightnessEvaluation, scheduleSource, themeLabel, sunTimes);

            var statusSummary = _settings.AutoThemeSwitching
                ? $"{ScheduleCalculator.GetPhaseLabel(brightnessEvaluation.Phase)} mode, {themeLabel} theme."
                : $"{ScheduleCalculator.GetPhaseLabel(brightnessEvaluation.Phase)} mode.";
            var statusDetail = $"{scheduleSource}. Next change at {brightnessEvaluation.NextChange:HH:mm}.";
            SetStatus(statusSummary, statusDetail);
        }
        finally
        {
            _isApplying = false;
        }
    }

    private void UpdateHeroSummary(ScheduleEvaluation evaluation, string scheduleSource, string? themeLabel, SunTimes? sunTimes)
    {
        _heroBrightnessValueLabel.Text = $"{evaluation.Brightness}%";
        _heroPhaseLabel.Text = ScheduleCalculator.GetPhaseLabel(evaluation.Phase).ToUpperInvariant();
        _heroNextChangeLabel.Text = evaluation.NextChange.ToString("HH:mm");
        _heroThemeLabel.Text = themeLabel == null ? "Live sync" : $"{themeLabel} synced";
        _heroScheduleSourceLabel.Text = scheduleSource;
        _heroSunWindowLabel.Text = sunTimes != null
            ? $"Sunrise {sunTimes.Sunrise:HH:mm}  •  Sunset {sunTimes.Sunset:HH:mm}"
            : $"Manual window {_settings.DayStartTime:hh\\:mm}  •  Night starts {_settings.NightStartTime:hh\\:mm}";
        UpdateThemeStatusSummary();
    }

    private void SetStatus(string status, string detail)
    {
        _statusLabel.Text = status;
        _statusDetailLabel.Text = detail;
    }

    private async Task<LocationResult?> ResolveLocationAsync(bool showMessages)
    {
        if (CanReuseResolvedLocation())
        {
            UpdateLocationLabel();
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
                "Unable to get sunrise and sunset from the server. Manual schedule will be used.",
                AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        return null;
    }

    private SunTimes? GetCachedSunTimesForPreview()
    {
        return _settings.UseSunSchedule &&
               _cachedSunTimes != null &&
               _cachedSunDate.Date == DateTime.Today
            ? _cachedSunTimes
            : null;
    }

    private bool CanReuseResolvedLocation()
    {
        return _lastLocation != null &&
               !string.Equals(_lastLocation.Source, "Last known", StringComparison.Ordinal) &&
               DateTime.UtcNow - _lastResolvedLocationAtUtc < LocationRefreshInterval;
    }

    private void RememberLocation(LocationResult location)
    {
        _lastLocation = location;
        _lastResolvedLocationAtUtc = DateTime.UtcNow;
        SaveLastLocation(location);
        UpdateLocationLabel();
    }

    private void InvalidateLocationCache()
    {
        _lastLocation = null;
        _lastResolvedLocationAtUtc = DateTime.MinValue;
        _cachedSunTimes = null;
        _cachedSunDate = DateTime.MinValue;
        _cachedLat = null;
        _cachedLon = null;
        UpdateLocationLabel();
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
            var formatted = FormatLocationLabel(
                _lastLocation.City,
                _lastLocation.Country,
                _lastLocation.Latitude,
                _lastLocation.Longitude,
                _lastLocation.Source);
            _locationLabel.Text = formatted;
            _heroLocationLabel.Text = formatted;
            return;
        }

        if (_settings.LastLatitude.HasValue && _settings.LastLongitude.HasValue)
        {
            var formatted = FormatLocationLabel(
                _settings.LastCity,
                _settings.LastCountry,
                _settings.LastLatitude.Value,
                _settings.LastLongitude.Value,
                "Last known");
            _locationLabel.Text = formatted;
            _heroLocationLabel.Text = formatted;
            return;
        }

        _locationLabel.Text = "No location yet. Add a city or allow geolocation to unlock sunrise and sunset mode.";
        _heroLocationLabel.Text = "No location yet. Add a city or allow geolocation to unlock sunrise and sunset mode.";
    }

    private static string FormatLocationLabel(string? city, string? country, double latitude, double longitude, string source)
    {
        var place = $"{city} {country}".Trim();
        if (string.IsNullOrWhiteSpace(place))
        {
            place = "Unknown location";
        }

        return $"{place}  •  {latitude:F4}, {longitude:F4}  •  via {source}";
    }

    private void ApplyCurrentTheme()
    {
        _palette = ThemeManager.CreatePalette();
        ThemeManager.ApplyTheme(this, _palette);
        ThemeManager.ApplyTheme(_trayMenu, _palette);
        ApplyLabelTheme(this);

        _dayStartPicker.CalendarForeColor = _palette.TextPrimary;
        _dayStartPicker.CalendarMonthBackground = _palette.SurfaceRaised;
        _dayStartPicker.CalendarTitleBackColor = _palette.SurfaceMuted;
        _dayStartPicker.CalendarTitleForeColor = _palette.TextPrimary;
        _dayStartPicker.CalendarTrailingForeColor = _palette.TextMuted;
        _dayStartPicker.ForeColor = _palette.TextPrimary;

        _nightStartPicker.CalendarForeColor = _palette.TextPrimary;
        _nightStartPicker.CalendarMonthBackground = _palette.SurfaceRaised;
        _nightStartPicker.CalendarTitleBackColor = _palette.SurfaceMuted;
        _nightStartPicker.CalendarTitleForeColor = _palette.TextPrimary;
        _nightStartPicker.CalendarTrailingForeColor = _palette.TextMuted;
        _nightStartPicker.ForeColor = _palette.TextPrimary;

        UpdateThemeStatusSummary();
        Invalidate(true);
    }

    private void ApplyLabelTheme(Control root)
    {
        if (root is Label label)
        {
            switch (label.Tag as string)
            {
                case "HeroTitle":
                    label.ForeColor = _palette.TextPrimary;
                    break;
                case "HeroSubtitle":
                    label.ForeColor = _palette.TextSecondary;
                    break;
                case "HeroValue":
                    label.ForeColor = _palette.TextPrimary;
                    break;
                case "HeroTag":
                    label.ForeColor = _palette.AccentStrong;
                    break;
                case "HeroBody":
                    label.ForeColor = _palette.TextSecondary;
                    break;
                case "HeroMetricCaption":
                    label.ForeColor = _palette.TextMuted;
                    break;
                case "SectionKicker":
                    label.ForeColor = _palette.AccentStrong;
                    break;
                case "SectionTitle":
                    label.ForeColor = _palette.TextPrimary;
                    break;
                case "SectionSubtitle":
                    label.ForeColor = _palette.TextSecondary;
                    break;
                case "MetaValue":
                    label.ForeColor = _palette.TextPrimary;
                    break;
                case "InputCaption":
                    label.ForeColor = _palette.TextMuted;
                    break;
                case "Divider":
                    label.BackColor = Color.FromArgb(_palette.Mode == AppColorMode.Dark ? 44 : 224, _palette.Border);
                    break;
                case "BodyPrimary":
                    label.ForeColor = _palette.TextPrimary;
                    break;
                case "BodySecondary":
                    label.ForeColor = _palette.TextSecondary;
                    break;
                case "FooterStatus":
                    label.ForeColor = _palette.TextPrimary;
                    break;
            }
        }

        foreach (Control child in root.Controls)
        {
            ApplyLabelTheme(child);
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!ThemeManager.IsThemeRelatedChange(e.Category) || IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke((Action)ApplyCurrentTheme);
            return;
        }

        ApplyCurrentTheme();
    }

    private void ShowMainWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ToggleEnabled()
    {
        _enabledToggle.Checked = !_enabledToggle.Checked;
    }

    private void ExitApplication()
    {
        _allowClose = true;
        _trayIcon.Visible = false;
        Close();
    }

    private static string FormatTransitionMinutes(int value) =>
        value == 0 ? "Instant" : $"{value} min";

    private static string FormatThemeLeadTime(int value) =>
        value == 0 ? "At sun shift" : $"{value} min early";

    private static int ClampTransitionMinutes(int value) => Math.Min(MaxTransitionMinutes, Math.Max(MinTransitionMinutes, value));

    private static int Clamp(int value) => Math.Min(100, Math.Max(0, value));

    private SettingToggleRow CreateToggle(string text, string description) =>
        new()
        {
            Text = text,
            Description = description,
            Margin = new Padding(0)
        };

    private MetricSlider CreateSlider(string text, string description, Func<int, string> formatter, int minimum, int maximum) =>
        new()
        {
            Text = text,
            Description = description,
            ValueFormatter = formatter,
            Minimum = minimum,
            Maximum = maximum,
            Margin = new Padding(0)
        };

    private DateTimePicker CreateTimePicker() =>
        new()
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "HH:mm",
            ShowUpDown = true,
            Width = 128,
            Height = 36,
            Font = _bodyFont
        };

    private Label CreateLabel(string text, string tag, Font font, bool autoSize) =>
        new()
        {
            Text = text,
            Tag = tag,
            Font = font,
            AutoSize = autoSize,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };

    private static Icon LoadAppIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        if (File.Exists(iconPath))
        {
            try
            {
                return new Icon(iconPath);
            }
            catch
            {
            }
        }

        try
        {
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    private static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
        return key?.GetValue(AutoRunValueName) != null;
    }

    private static void EnableAutoStart()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        key?.SetValue(AutoRunValueName, $"\"{Application.ExecutablePath}\"");
    }

    private static void DisableAutoStart()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        key?.DeleteValue(AutoRunValueName, false);
    }
}
