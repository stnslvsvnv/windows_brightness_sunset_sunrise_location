using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NotebookAutoBrightnessInstaller;

public sealed class InstallerForm : Form
{
    private readonly Label _statusLabel;
    private readonly Button _installButton;
    private readonly Button _uninstallButton;
    private readonly CheckBox _launchCheckBox;
    private readonly ProgressBar _progressBar;
    private readonly TextBox _installPathBox;
    private readonly Button _browseButton;

    public InstallerForm()
    {
        Text = InstallerOperations.AppDisplayName + " Setup";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 300);

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

        var titleLabel = new Label
        {
            Text = "Notebook sunrise/sunset auto brightness",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 12, FontStyle.Bold)
        };

        _installPathBox = new TextBox { Width = 360, ReadOnly = true };
        _browseButton = new Button { Text = "Change...", Width = 90 };

        _statusLabel = new Label { AutoSize = true };
        _progressBar = new ProgressBar { Style = ProgressBarStyle.Continuous, Width = 420, Height = 18 };
        _launchCheckBox = new CheckBox { Text = "Launch after install", AutoSize = true, Checked = true };

        _installButton = new Button { Text = "Install", Width = 120 };
        _uninstallButton = new Button { Text = "Uninstall", Width = 120 };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 2,
            RowCount = 6
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        layout.Controls.Add(titleLabel, 0, 0);
        layout.SetColumnSpan(titleLabel, 2);

        layout.Controls.Add(new Label { Text = "Install path", AutoSize = true }, 0, 1);
        layout.Controls.Add(_installPathBox, 0, 2);
        layout.Controls.Add(_browseButton, 1, 2);

        layout.Controls.Add(_progressBar, 0, 3);
        layout.SetColumnSpan(_progressBar, 2);

        layout.Controls.Add(_statusLabel, 0, 4);
        layout.SetColumnSpan(_statusLabel, 2);

        var buttonPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        buttonPanel.Controls.Add(_installButton);
        buttonPanel.Controls.Add(_uninstallButton);
        buttonPanel.Controls.Add(_launchCheckBox);
        layout.Controls.Add(buttonPanel, 0, 5);
        layout.SetColumnSpan(buttonPanel, 2);

        Controls.Add(layout);

        _installButton.Click += async (_, _) => await RunInstallAsync();
        _uninstallButton.Click += (_, _) => InstallerOperations.UninstallInteractive();
        _browseButton.Click += (_, _) => ChooseInstallPath();

        Load += (_, _) => InitializeState();
    }

    private void InitializeState()
    {
        _installPathBox.Text = InstallerOperations.GetInstallLocation() ?? InstallerOperations.DefaultInstallDir;
        _statusLabel.Text = InstallerOperations.IsInstalled()
            ? "Status: Installed"
            : "Status: Not installed";
        _uninstallButton.Enabled = InstallerOperations.IsInstalled();
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
        _installButton.Enabled = false;
        _uninstallButton.Enabled = false;
        _browseButton.Enabled = false;
        _progressBar.Style = ProgressBarStyle.Marquee;
        _statusLabel.Text = "Installing...";

        var installDir = _installPathBox.Text.Trim();

        try
        {
            var appPath = await Task.Run(() => InstallerOperations.Install(installDir));
            _statusLabel.Text = "Installation complete.";
            _progressBar.Style = ProgressBarStyle.Continuous;

            var launch = false;
            if (_launchCheckBox.Checked)
            {
                var result = MessageBox.Show(
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

            InitializeState();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Installation failed.";
            MessageBox.Show(
                $"Installation failed.\n\n{ex.Message}",
                InstallerOperations.AppDisplayName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _progressBar.Style = ProgressBarStyle.Continuous;
            _installButton.Enabled = true;
            _uninstallButton.Enabled = InstallerOperations.IsInstalled();
            _browseButton.Enabled = true;
        }
    }
}
