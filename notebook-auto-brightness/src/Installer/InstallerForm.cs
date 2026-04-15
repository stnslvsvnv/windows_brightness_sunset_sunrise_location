using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using NotebookAutoBrightness.Ui;

namespace NotebookAutoBrightnessInstaller;

public sealed class InstallerForm : Form
{
    private readonly Font _heroTitleFont = new("Segoe UI Semibold", 16F, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _heroSubtitleFont = new("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _sectionKickerFont = new("Segoe UI Semibold", 8F, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _sectionTitleFont = new("Segoe UI Semibold", 12.25F, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _sectionSubtitleFont = new("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _bodyFont = new("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _inputCaptionFont = new("Segoe UI Semibold", 8F, FontStyle.Regular, GraphicsUnit.Point);

    private readonly Label _heroTitleLabel;
    private readonly Label _heroSubtitleLabel;
    private readonly Label _heroStateLabel;
    private readonly Label _pathPreviewLabel;
    private readonly Label _statusLabel;
    private readonly Label _noteLabel;
    private readonly TextBox _installPathBox;
    private readonly ThemedButton _browseButton;
    private readonly ThemedButton _installButton;
    private readonly ThemedButton _uninstallButton;
    private readonly SettingToggleRow _launchToggle;
    private readonly SettingToggleRow _autoStartToggle;
    private readonly ProgressBar _progressBar;

    private ThemePalette _palette = ThemeManager.CreatePalette();

    public InstallerForm()
    {
        Text = InstallerOperations.AppDisplayName + " Setup";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(720, 420);
        DoubleBuffered = true;

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        Icon = LoadAppIcon();

        _heroTitleLabel = CreateLabel("Notebook Auto Brightness", "HeroTitle", _heroTitleFont, autoSize: true);
        _heroSubtitleLabel = CreateLabel("Minimal setup for daylight-driven brightness and theme automation.", "HeroSubtitle", _heroSubtitleFont, autoSize: true);
        _heroStateLabel = CreateLabel("Ready to install", "HeroBody", _bodyFont, autoSize: true);
        _heroStateLabel.MaximumSize = new Size(190, 0);
        _pathPreviewLabel = CreateLabel(string.Empty, "HeroBody", _bodyFont, autoSize: true);
        _pathPreviewLabel.MaximumSize = new Size(320, 0);

        _installPathBox = new TextBox
        {
            Dock = DockStyle.Top,
            ReadOnly = true,
            Height = 36,
            Font = _bodyFont
        };

        _browseButton = new ThemedButton
        {
            Text = "Change path",
            VisualKind = ButtonVisualKind.Secondary,
            Width = 138
        };

        _installButton = new ThemedButton
        {
            Text = "Install",
            VisualKind = ButtonVisualKind.Primary,
            Width = 150
        };

        _uninstallButton = new ThemedButton
        {
            Text = "Uninstall",
            VisualKind = ButtonVisualKind.Secondary,
            Width = 150
        };

        _launchToggle = CreateToggle("Launch after install", "Open the app immediately when setup finishes.");
        _launchToggle.Checked = true;

        _autoStartToggle = CreateToggle("Start with Windows", "Write the autorun entry during installation.");
        _autoStartToggle.Checked = true;

        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 16,
            Style = ProgressBarStyle.Continuous
        };

        _statusLabel = CreateLabel("Ready.", "BodyPrimary", new Font("Segoe UI Semibold", 10F, FontStyle.Regular, GraphicsUnit.Point), autoSize: true);
        _noteLabel = CreateLabel("You can also uninstall an existing copy from here.", "BodySecondary", _bodyFont, autoSize: true);
        _noteLabel.MaximumSize = new Size(220, 0);

        Controls.Add(BuildLayout());

        _installButton.Click += async (_, _) => await RunInstallAsync();
        _uninstallButton.Click += (_, _) => InstallerOperations.UninstallInteractive();
        _browseButton.Click += (_, _) => ChooseInstallPath();
        Load += (_, _) => InitializeState();

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        ApplyCurrentTheme();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var backgroundBrush = new LinearGradientBrush(ClientRectangle, _palette.WindowBackground, _palette.WindowBackgroundAlt, 90f);
        e.Graphics.FillRectangle(backgroundBrush, ClientRectangle);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }

        base.Dispose(disposing);
    }

    private TableLayoutPanel BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 18, 20, 18),
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var header = BuildHeaderStrip();
        header.Margin = new Padding(0, 0, 0, 14);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(BuildContentSurface(), 0, 1);

        return root;
    }

    private Control BuildHeaderStrip()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var leftStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        leftStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftStack.Controls.Add(_heroTitleLabel, 0, 0);
        leftStack.Controls.Add(_heroSubtitleLabel, 0, 1);

        var rightStack = new TableLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        rightStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightStack.Controls.Add(CreateLabel("STATE", "HeroMetricCaption", _sectionKickerFont, autoSize: true));
        rightStack.Controls.Add(_heroStateLabel);

        layout.Controls.Add(leftStack, 0, 0);
        layout.Controls.Add(rightStack, 1, 0);
        return layout;
    }

    private ThemedCardPanel BuildContentSurface()
    {
        var card = new ThemedCardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1F));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
        content.Controls.Add(BuildDestinationPane(), 0, 0);
        content.Controls.Add(CreateVerticalDivider(), 1, 0);
        content.Controls.Add(BuildActionPane(), 2, 0);

        card.Controls.Add(content);
        return card;
    }

    private Control BuildDestinationPane()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Color.Transparent
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 3,
            AutoSize = true,
            BackColor = Color.Transparent,
            Padding = new Padding(18, 18, 18, 0)
        };
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.Controls.Add(CreateLabel("Destination", "SectionTitle", _sectionTitleFont, autoSize: true), 0, 0);
        var subtitle = CreateLabel("Choose where the app lives and how it behaves after install.", "SectionSubtitle", _sectionSubtitleFont, autoSize: true);
        subtitle.MaximumSize = new Size(300, 0);
        header.Controls.Add(subtitle, 0, 1);
        _pathPreviewLabel.Margin = new Padding(0, 8, 0, 0);
        header.Controls.Add(_pathPreviewLabel, 0, 2);

        var pathPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true,
            BackColor = Color.Transparent,
            Padding = new Padding(18, 10, 18, 8),
            Margin = new Padding(0)
        };
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathPanel.Controls.Add(CreateLabel("INSTALL PATH", "InputCaption", _inputCaptionFont, autoSize: true), 0, 0);
        pathPanel.SetColumnSpan(pathPanel.Controls[0], 2);
        pathPanel.Controls.Add(_installPathBox, 0, 1);
        pathPanel.Controls.Add(_browseButton, 1, 1);

        _launchToggle.Dock = DockStyle.Fill;
        _autoStartToggle.Dock = DockStyle.Fill;

        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(pathPanel, 0, 1);
        layout.Controls.Add(CreateFillDivider(), 0, 2);
        layout.Controls.Add(_launchToggle, 0, 3);
        layout.Controls.Add(_autoStartToggle, 0, 4);

        return layout;
    }

    private Control BuildActionPane()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Color.Transparent,
            Padding = new Padding(18, 18, 18, 18)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            BackColor = Color.Transparent
        };
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.Controls.Add(CreateLabel("Setup", "SectionTitle", _sectionTitleFont, autoSize: true), 0, 0);
        var subtitle = CreateLabel("Install, replace or remove the current build from one place.", "SectionSubtitle", _sectionSubtitleFont, autoSize: true);
        subtitle.MaximumSize = new Size(220, 0);
        header.Controls.Add(subtitle, 0, 1);

        var statusStack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 3,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 12, 0, 0)
        };
        statusStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statusStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statusStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statusStack.Controls.Add(_progressBar, 0, 0);
        statusStack.Controls.Add(_statusLabel, 0, 1);
        statusStack.Controls.Add(_noteLabel, 0, 2);

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        buttonRow.Controls.Add(_installButton);
        buttonRow.Controls.Add(_uninstallButton);

        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(statusStack, 0, 1);
        layout.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent }, 0, 3);
        layout.Controls.Add(buttonRow, 0, 4);

        return layout;
    }

    private Control CreateFillDivider() =>
        new Label
        {
            Tag = "Divider",
            Dock = DockStyle.Fill,
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

    private ThemedCardPanel CreateSectionCard(string kicker, string title, string subtitle, Control content)
    {
        var card = new ThemedCardPanel
        {
            Tone = SurfaceTone.Standard,
            Dock = DockStyle.Fill
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 3,
            AutoSize = true,
            BackColor = Color.Transparent
        };
        header.Controls.Add(CreateLabel(kicker, "SectionKicker", _sectionKickerFont, autoSize: true), 0, 0);
        header.Controls.Add(CreateLabel(title, "SectionTitle", _sectionTitleFont, autoSize: true), 0, 1);
        header.Controls.Add(CreateLabel(subtitle, "SectionSubtitle", _sectionSubtitleFont, autoSize: true), 0, 2);

        content.Dock = DockStyle.Fill;
        content.Margin = new Padding(0, 12, 0, 0);

        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(content, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private void InitializeState()
    {
        _installPathBox.Text = InstallerOperations.GetInstallLocation() ?? InstallerOperations.DefaultInstallDir;
        _pathPreviewLabel.Text = _installPathBox.Text;

        var installed = InstallerOperations.IsInstalled();
        _heroStateLabel.Text = installed ? "Installed build detected" : "Ready to install";
        _statusLabel.Text = installed ? "Existing installation found." : "No installation found yet.";
        _noteLabel.Text = installed
            ? "You can reinstall in place or remove the current version."
            : "Install once, then manage everything from the tray app.";
        _uninstallButton.Enabled = installed;
    }

    private void ChooseInstallPath()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select installation folder",
            SelectedPath = _installPathBox.Text
        };

        if (dialog.ShowDialog(this) == DialogResult.OK && Directory.Exists(dialog.SelectedPath))
        {
            _installPathBox.Text = dialog.SelectedPath;
            _pathPreviewLabel.Text = dialog.SelectedPath;
        }
    }

    private async Task RunInstallAsync()
    {
        SetBusyState(true, "Installing...", "Copying files and preparing autorun settings.");
        var installDir = _installPathBox.Text.Trim();

        try
        {
            var appPath = await Task.Run(() => InstallerOperations.Install(installDir, true, _autoStartToggle.Checked));
            _statusLabel.Text = "Installation complete.";
            _noteLabel.Text = "The app is ready. You can launch it now or find it later in the install folder.";
            _progressBar.Style = ProgressBarStyle.Continuous;
            InitializeState();

            var launch = false;
            if (_launchToggle.Checked)
            {
                var result = MessageBox.Show(
                    this,
                    "Everything is ready. Launch now?",
                    InstallerOperations.AppDisplayName,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                launch = result == DialogResult.Yes;
            }

            if (launch && File.Exists(appPath))
            {
                Process.Start(new ProcessStartInfo(appPath) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Installation failed.";
            _noteLabel.Text = "Setup could not finish. Check the error details and try again.";
            MessageBox.Show(
                this,
                $"Installation failed.\n\n{ex.Message}",
                InstallerOperations.AppDisplayName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetBusyState(false, _statusLabel.Text, _noteLabel.Text);
            InitializeState();
        }
    }

    private void SetBusyState(bool busy, string status, string note)
    {
        _installButton.Enabled = !busy;
        _uninstallButton.Enabled = !busy && InstallerOperations.IsInstalled();
        _browseButton.Enabled = !busy;
        _launchToggle.Enabled = !busy;
        _autoStartToggle.Enabled = !busy;
        _statusLabel.Text = status;
        _noteLabel.Text = note;
        _progressBar.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
    }

    private void ApplyCurrentTheme()
    {
        _palette = ThemeManager.CreatePalette();
        ThemeManager.ApplyTheme(this, _palette);
        ApplyLabelTheme(this);
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
                case "HeroBody":
                    label.ForeColor = _palette.TextPrimary;
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

    private SettingToggleRow CreateToggle(string text, string description) =>
        new()
        {
            Text = text,
            Description = description,
            Margin = new Padding(0)
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
}
