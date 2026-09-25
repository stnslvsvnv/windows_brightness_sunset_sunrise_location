using System;
using System.Windows.Forms;
using Microsoft.Win32;
using NotebookAutoBrightness.Ui;
using Timer = System.Windows.Forms.Timer;

namespace NotebookAutoBrightness;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly AutomationController _controller;
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _trayMenu;
    private readonly ToolStripMenuItem _openSettingsItem;
    private readonly ToolStripMenuItem _applyNowItem;
    private readonly ToolStripMenuItem _toggleAutomationItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly Timer _startupTimer;
    private readonly Timer _automationTimer;
    private readonly Timer _reapplyTimer;
    private readonly bool _showSettingsOnStart;

    private ThemePalette _palette = ThemeManager.CreatePalette();
    private MainForm? _settingsForm;
    private bool _disposed;

    public TrayApplicationContext(bool showSettingsOnStart)
    {
        _showSettingsOnStart = showSettingsOnStart;
        _controller = new AutomationController(Application.ExecutablePath);
        _controller.StatusChanged += HandleStatusChanged;
        _controller.SettingsChanged += HandleSettingsChanged;
        _controller.ThemeChanged += HandleThemeChanged;

        _trayMenu = new ContextMenuStrip();
        _openSettingsItem = new ToolStripMenuItem("Open settings", null, (_, _) => ShowSettingsWindow());
        _applyNowItem = new ToolStripMenuItem("Apply now", null, async (_, _) => await SafeApplyAsync(forceBrightness: true, showMessages: true));
        _toggleAutomationItem = new ToolStripMenuItem("Disable automation", null, async (_, _) => await ToggleAutomationAsync());
        _exitItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication());

        _trayMenu.Items.AddRange(new ToolStripItem[]
        {
            _openSettingsItem,
            _applyNowItem,
            _toggleAutomationItem,
            new ToolStripSeparator(),
            _exitItem
        });

        _trayIcon = new NotifyIcon
        {
            Icon = AppRuntime.LoadAppIcon(),
            Text = TruncateTooltip(AppRuntime.AppName),
            Visible = true,
            ContextMenuStrip = _trayMenu
        };
        _trayIcon.DoubleClick += (_, _) => ShowSettingsWindow();

        _automationTimer = new Timer { Interval = 15_000 };
        _automationTimer.Tick += async (_, _) => await SafeApplyAsync(forceBrightness: false);

        _reapplyTimer = new Timer { Interval = 2_000 };
        _reapplyTimer.Tick += async (_, _) =>
        {
            _reapplyTimer.Stop();
            await SafeApplyAsync(forceBrightness: true);
        };

        _startupTimer = new Timer { Interval = 1 };
        _startupTimer.Tick += async (_, _) => await SafeFinishStartupAsync();
        _startupTimer.Start();

        ApplyCheckInterval();
        UpdateToggleMenuText();
        SystemEvents.UserPreferenceChanged += HandleUserPreferenceChanged;
        SystemEvents.PowerModeChanged += HandlePowerModeChanged;
        SystemEvents.SessionSwitch += HandleSessionSwitch;
        SystemEvents.TimeChanged += HandleTimeChanged;
        SystemEvents.DisplaySettingsChanged += HandleDisplaySettingsChanged;
        ApplyCurrentTheme();
    }

    protected override void ExitThreadCore()
    {
        DisposeResources();
        base.ExitThreadCore();
    }

    private async Task FinishStartupAsync()
    {
        _startupTimer.Stop();
        await _controller.InitializeAsync();
        _automationTimer.Start();

        if (_showSettingsOnStart)
        {
            ShowSettingsWindow();
        }
    }

    private void ShowSettingsWindow()
    {
        if (_settingsForm == null || _settingsForm.IsDisposed)
        {
            _settingsForm = new MainForm(_controller);
            _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        }

        if (!_settingsForm.Visible)
        {
            _settingsForm.Show();
        }

        if (_settingsForm.WindowState == FormWindowState.Minimized)
        {
            _settingsForm.WindowState = FormWindowState.Normal;
        }

        _settingsForm.Activate();
    }

    private async Task ToggleAutomationAsync()
    {
        try
        {
            _controller.ToggleEnabled();
            UpdateToggleMenuText();
            await _controller.ApplyScheduleAsync(false);
        }
        catch (Exception ex)
        {
            AppLog.WriteError("toggle automation", ex);
        }
    }

    // Timer ticks and menu clicks are async void: an exception there used to become an unhandled
    // failure (dialog or dead process), so every entry point goes through a guarded wrapper.
    private async Task SafeApplyAsync(bool forceBrightness, bool showMessages = false)
    {
        try
        {
            await _controller.ApplyScheduleAsync(showMessages, owner: null, forceBrightness: forceBrightness);
        }
        catch (Exception ex)
        {
            AppLog.WriteError("apply schedule", ex);
        }
    }

    private async Task SafeFinishStartupAsync()
    {
        try
        {
            await FinishStartupAsync();
        }
        catch (Exception ex)
        {
            AppLog.WriteError("startup", ex);
        }
    }

    private void HandlePowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume || e.Mode == PowerModes.StatusChange)
        {
            RequestImmediateApply($"power {e.Mode}");
        }
    }

    private void HandleSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            RequestImmediateApply("session unlocked");
        }
    }

    private void HandleTimeChanged(object? sender, EventArgs e) => RequestImmediateApply("clock or time zone changed");

    private void HandleDisplaySettingsChanged(object? sender, EventArgs e) => RequestImmediateApply("display settings changed");

    // System events arrive on their own thread and several can fire at once on a wake-up, so the
    // work is marshalled to the UI thread and coalesced through the existing one-shot timer.
    private void RequestImmediateApply(string reason)
    {
        if (_disposed)
        {
            return;
        }

        if (_trayMenu.InvokeRequired)
        {
            _trayMenu.BeginInvoke(new Action(() => RequestImmediateApply(reason)));
            return;
        }

        _controller.HandleSystemEvent(reason);
        _reapplyTimer.Stop();
        _reapplyTimer.Start();
    }

    private void HandleStatusChanged(AutomationStatus status)
    {
        var tooltip = status.Brightness.HasValue && status.Phase.HasValue
            ? $"{AppRuntime.AppName} | {ScheduleCalculator.GetPhaseLabel(status.Phase.Value)} | {status.Brightness.Value}%{BuildSourceSuffix(status.ScheduleSource)}"
            : $"{AppRuntime.AppName} | {status.StatusSummary}";
        _trayIcon.Text = TruncateTooltip(tooltip);
    }

    private void HandleThemeChanged()
    {
        // The theme broadcast makes the display stack re-apply its own brightness a moment later,
        // so give it a beat and then write our value again.
        _reapplyTimer.Stop();
        _reapplyTimer.Start();
    }

    private static string BuildSourceSuffix(string scheduleSource) =>
        scheduleSource == AutomationController.SunScheduleSource
            ? string.Empty
            : " | manual";

    private void HandleSettingsChanged()
    {
        UpdateToggleMenuText();
        ApplyCheckInterval();
        // A settings change must be visible right away, not on the next tick.
        RequestImmediateApply("settings changed");
    }

    private void ApplyCheckInterval()
    {
        _automationTimer.Interval = _controller.GetSettings().CheckIntervalSeconds * 1_000;
    }

    private void UpdateToggleMenuText()
    {
        _toggleAutomationItem.Text = _controller.GetSettings().Enabled
            ? "Disable automation"
            : "Enable automation";
    }

    private void ExitApplication()
    {
        DisposeResources();
        ExitThread();
    }

    private void DisposeResources()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _startupTimer.Stop();
        _automationTimer.Stop();
        _reapplyTimer.Stop();

        if (_settingsForm != null && !_settingsForm.IsDisposed)
        {
            _settingsForm.Close();
            _settingsForm.Dispose();
        }

        _trayIcon.Visible = false;
        _controller.StatusChanged -= HandleStatusChanged;
        _controller.SettingsChanged -= HandleSettingsChanged;
        _controller.ThemeChanged -= HandleThemeChanged;
        SystemEvents.UserPreferenceChanged -= HandleUserPreferenceChanged;
        SystemEvents.PowerModeChanged -= HandlePowerModeChanged;
        SystemEvents.SessionSwitch -= HandleSessionSwitch;
        SystemEvents.TimeChanged -= HandleTimeChanged;
        SystemEvents.DisplaySettingsChanged -= HandleDisplaySettingsChanged;
        _startupTimer.Dispose();
        _automationTimer.Dispose();
        _reapplyTimer.Dispose();
        _trayIcon.Dispose();
        _openSettingsItem.Dispose();
        _applyNowItem.Dispose();
        _toggleAutomationItem.Dispose();
        _exitItem.Dispose();
        _trayMenu.Dispose();
    }

    private static string TruncateTooltip(string text) =>
        text.Length <= 63 ? text : text[..63];

    private void ApplyCurrentTheme()
    {
        _palette = ThemeManager.CreatePalette();
        ThemeManager.ApplyTheme(_trayMenu, _palette);
    }

    private void HandleUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!ThemeManager.IsThemeRelatedChange(e.Category) || _disposed)
        {
            return;
        }

        if (_trayMenu.InvokeRequired)
        {
            _trayMenu.BeginInvoke(new Action(ApplyCurrentTheme));
            return;
        }

        ApplyCurrentTheme();
    }
}
