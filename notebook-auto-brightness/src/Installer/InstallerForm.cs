using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using NotebookAutoBrightness.Ui;

namespace NotebookAutoBrightnessInstaller;

public sealed class InstallerForm : Form
{
    private readonly TextBox _installPathBox;
    private readonly CheckBox _launchCheck;
    private readonly CheckBox _autoStartCheck;
    private readonly Label _installedStateLabel;
    private readonly Label _statusLabel;
    private readonly Label _statusDetailLabel;
    private readonly ProgressBar _progressBar;
    private readonly Button _browseButton;
    private readonly Button _installButton;
    private readonly Button _uninstallButton;

    private ThemePalette _palette = ThemeManager.CreatePalette();

    public InstallerForm()
    {
        Text = InstallerOperations.AppDisplayName + " Setup";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(720, 500);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        Icon = LoadAppIcon();

        _installPathBox = new TextBox
        {
            Width = 410,
            ReadOnly = true
        };

        _launchCheck = new CheckBox
        {
            Text = "Launch app after install",
            AutoSize = true,
            Checked = true
        };

        _autoStartCheck = new CheckBox
        {
            Text = "Start with Windows in background",
            AutoSize = true,
            Checked = true
        };

        _installedStateLabel = new Label
        {
            AutoSize = false,
            Width = 620,
            Height = 44
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

        _progressBar = new ProgressBar
        {
            Width = 620,
            Height = 18,
            Style = ProgressBarStyle.Continuous
        };

        _browseButton = new Button
        {
            Text = "Change path",
            Width = 128,
            Tag = "SecondaryButton"
        };

        _installButton = new Button
        {
            Text = "Install",
            Width = 128
        };

        _uninstallButton = new Button
        {
            Text = "Uninstall",
            Width = 128,
            Tag = "SecondaryButton"
        };

        Controls.Add(BuildLayout());

        _browseButton.Click += (_, _) => ChooseInstallPath();
        _installButton.Click += async (_, _) => await RunInstallAsync();
        _uninstallButton.Click += (_, _) =>
        {
            InstallerOperations.UninstallInteractive();
            InitializeState();
        };

        Load += (_, _) => InitializeState();
        SystemEvents.UserPreferenceChanged += HandleUserPreferenceChanged;
        ApplyCurrentTheme();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
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
            RowCount = 5
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var noteLabel = new Label
        {
            AutoSize = false,
            Width = 680,
            Height = 40,
            Text = "Same lightweight tray app, same settings style. Install once, then let it run in the background."
        };

        root.Controls.Add(noteLabel, 0, 0);
        root.Controls.Add(BuildOverviewGroup(), 0, 1);
        root.Controls.Add(BuildDestinationGroup(), 0, 2);
        root.Controls.Add(BuildOptionsGroup(), 0, 3);
        root.Controls.Add(BuildActionsGroup(), 0, 4);

        return root;
    }

    private Control BuildOverviewGroup()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };

        panel.Controls.Add(_installedStateLabel);
        return WrapGroup("Overview", panel);
    }

    private Control BuildDestinationGroup()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            AutoSize = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 430F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var caption = new Label
        {
            Text = "Install path",
            AutoSize = true
        };

        layout.Controls.Add(caption, 0, 0);
        layout.Controls.Add(_installPathBox, 1, 0);
        layout.Controls.Add(_browseButton, 2, 0);

        return WrapGroup("Destination", layout);
    }

    private Control BuildOptionsGroup()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };

        panel.Controls.Add(_launchCheck);
        panel.Controls.Add(_autoStartCheck);
        return WrapGroup("Options", panel);
    }

    private Control BuildActionsGroup()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            AutoSize = true
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        buttonRow.Controls.Add(_installButton);
        buttonRow.Controls.Add(_uninstallButton);

        layout.Controls.Add(_progressBar, 0, 0);
        layout.Controls.Add(_statusLabel, 0, 1);
        layout.Controls.Add(_statusDetailLabel, 0, 2);
        layout.Controls.Add(buttonRow, 0, 3);

        return WrapGroup("Setup", layout);
    }

    private void InitializeState()
    {
        _installPathBox.Text = InstallerOperations.GetInstallLocation() ?? InstallerOperations.DefaultInstallDir;

        var installed = InstallerOperations.IsInstalled();
        _installedStateLabel.Text = installed
            ? "Installed build detected. You can reinstall over it or remove it from here."
            : "No installation found yet. Choose a path and install the tray app.";
        _statusLabel.Text = installed ? "Ready to update or uninstall." : "Ready to install.";
        _statusDetailLabel.Text = installed
            ? "Existing files were detected in the install location."
            : "The installer will copy the app and optional autorun configuration.";
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
        }
    }

    private async Task RunInstallAsync()
    {
        SetBusyState(true, "Installing...", "Copying files and preparing background autorun settings.");
        var installDir = _installPathBox.Text.Trim();

        try
        {
            var appPath = await Task.Run(() => InstallerOperations.Install(installDir, true, _autoStartCheck.Checked));
            InitializeState();
            _statusLabel.Text = "Installation complete.";
            _statusDetailLabel.Text = "The tray app is ready. It can start immediately or stay dormant until sign-in.";
            _progressBar.Style = ProgressBarStyle.Continuous;

            if (_launchCheck.Checked && File.Exists(appPath))
            {
                var result = MessageBox.Show(
                    this,
                    "Everything is ready. Launch the app now?",
                    InstallerOperations.AppDisplayName,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    Process.Start(new ProcessStartInfo(appPath) { UseShellExecute = true });
                }
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Installation failed.";
            _statusDetailLabel.Text = "Setup could not finish. Check the error details and try again.";
            MessageBox.Show(
                this,
                $"Installation failed.\n\n{ex.Message}",
                InstallerOperations.AppDisplayName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetBusyState(false, _statusLabel.Text, _statusDetailLabel.Text);
            InitializeState();
        }
    }

    private void SetBusyState(bool busy, string status, string detail)
    {
        _browseButton.Enabled = !busy;
        _installButton.Enabled = !busy;
        _uninstallButton.Enabled = !busy && InstallerOperations.IsInstalled();
        _launchCheck.Enabled = !busy;
        _autoStartCheck.Enabled = !busy;
        _statusLabel.Text = status;
        _statusDetailLabel.Text = detail;
        _progressBar.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
    }

    private void ApplyCurrentTheme()
    {
        _palette = ThemeManager.CreatePalette();
        ThemeManager.ApplyTheme(this, _palette);
        _installedStateLabel.ForeColor = _palette.TextSecondary;
        _statusDetailLabel.ForeColor = _palette.TextSecondary;
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
