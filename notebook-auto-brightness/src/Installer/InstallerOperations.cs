using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NotebookAutoBrightnessInstaller;

public static class InstallerOperations
{
    public const string AppDisplayName = "Notebook sunrise/sunset auto brightness";
    public const string AppExeName = "NotebookSunriseSunsetAutoBrightness.exe";
    public const string UninstallExeName = "uninstall.exe";
    public const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\NotebookSunriseSunsetAutoBrightness";
    public const string AutoRunValueName = "NotebookSunriseSunsetAutoBrightness";

    public static string DefaultInstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "Notebook Sunrise Sunset Auto Brightness");

    public static string? GetInstallLocation()
    {
        using var key = Registry.LocalMachine.OpenSubKey(UninstallKeyPath, false);
        return key?.GetValue("InstallLocation") as string;
    }

    public static bool IsInstalled()
    {
        var installDir = GetInstallLocation();
        return !string.IsNullOrWhiteSpace(installDir) && File.Exists(Path.Combine(installDir, AppExeName));
    }

    public static string Install(string installDir, bool createShortcut = true, bool autoStart = true)
    {
        CloseRunningApp();
        Directory.CreateDirectory(installDir);
        ExtractPayload(installDir);

        var installerPath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        var uninstallPath = Path.Combine(installDir, UninstallExeName);
        if (!string.IsNullOrWhiteSpace(installerPath))
        {
            File.Copy(installerPath, uninstallPath, true);
        }

        var appPath = Path.Combine(installDir, AppExeName);
        
        // Create shortcut in Start Menu
        if (createShortcut)
        {
            CreateStartMenuShortcut(appPath);
        }
        
        // Enable auto-start if requested
        if (autoStart)
        {
            EnableAutoStart(appPath);
        }
        
        WriteUninstallRegistry(installDir, uninstallPath, appPath);
        return appPath;
    }

    private static void CreateStartMenuShortcut(string appPath)
    {
        try
        {
            var startMenuDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
            var programsDir = Path.Combine(startMenuDir, "Programs");
            var appDir = Path.Combine(programsDir, "Notebook Auto Brightness");
            Directory.CreateDirectory(appDir);

            var shortcutPath = Path.Combine(appDir, "Notebook Auto Brightness.lnk");
            CreateShortcut(shortcutPath, appPath, appPath, 0);
        }
        catch
        {
            // Ignore shortcut creation failures
        }
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string iconPath, int iconIndex)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            
            var shell = Activator.CreateInstance(shellType);
            var createShortcutMethod = shellType.GetMethod("CreateShortcut");
            var shortcut = createShortcutMethod?.Invoke(shell, new object[] { shortcutPath });
            
            if (shortcut != null)
            {
                var targetPathProperty = shortcut.GetType().GetProperty("TargetPath");
                var iconLocationProperty = shortcut.GetType().GetProperty("IconLocation");
                var descriptionProperty = shortcut.GetType().GetProperty("Description");
                var workingDirectoryProperty = shortcut.GetType().GetProperty("WorkingDirectory");
                var saveMethod = shortcut.GetType().GetMethod("Save");
                
                targetPathProperty?.SetValue(shortcut, targetPath);
                iconLocationProperty?.SetValue(shortcut, $"{iconPath},{iconIndex}");
                descriptionProperty?.SetValue(shortcut, "Auto-adjust screen brightness by sunrise/sunset");
                workingDirectoryProperty?.SetValue(shortcut, Path.GetDirectoryName(targetPath));
                saveMethod?.Invoke(shortcut, null);
            }
        }
        catch
        {
            // Ignore shortcut creation failures
        }
    }

    private static void EnableAutoStart(string appPath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            key?.SetValue(AutoRunValueName, $"\"{appPath}\" --background");
        }
        catch
        {
            // Ignore auto-start failures
        }
    }

    public static void UninstallInteractive()
    {
        var installDir = GetInstallLocation() ?? DefaultInstallDir;
        if (!Directory.Exists(installDir))
        {
            MessageBox.Show("Application is not installed.", AppDisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(
                "Uninstall Notebook sunrise/sunset auto brightness?",
                AppDisplayName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var currentPath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(currentPath) &&
            currentPath.StartsWith(installDir, StringComparison.OrdinalIgnoreCase))
        {
            BootstrapUninstall(installDir);
            return;
        }

        try
        {
            UninstallCore(installDir);
            MessageBox.Show("Uninstall completed.", AppDisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Uninstall failed.\n\n{ex.Message}", AppDisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public static void UninstallRunner(string installDir, string selfPath)
    {
        try
        {
            UninstallCore(installDir);
            MessageBox.Show("Uninstall completed.", AppDisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Uninstall failed.\n\n{ex.Message}", AppDisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            ScheduleSelfDelete(selfPath);
        }
    }

    private static void BootstrapUninstall(string installDir)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "NotebookAutoBrightnessUninstall");
        Directory.CreateDirectory(tempDir);
        var tempExe = Path.Combine(tempDir, UninstallExeName);

        var currentPath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            File.Copy(currentPath, tempExe, true);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = tempExe,
            Arguments = $"/uninstall-runner \"{installDir}\" \"{tempExe}\"",
            UseShellExecute = true
        };
        Process.Start(startInfo);
    }

    private static void UninstallCore(string installDir)
    {
        CloseRunningApp();
        RemoveAutoStart();
        RemoveStartMenuShortcut();

        if (Directory.Exists(installDir))
        {
            Directory.Delete(installDir, true);
        }

        RemoveUninstallRegistry();
    }

    private static void RemoveStartMenuShortcut()
    {
        try
        {
            var startMenuDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
            var programsDir = Path.Combine(startMenuDir, "Programs");
            var appDir = Path.Combine(programsDir, "Notebook Auto Brightness");
            if (Directory.Exists(appDir))
            {
                Directory.Delete(appDir, true);
            }
        }
        catch
        {
            // Ignore shortcut removal failures
        }
    }

    private static void ExtractPayload(string installDir)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("Installer.payload.zip");
        if (stream == null)
        {
            throw new InvalidOperationException("Installer payload not found. Rebuild the installer with payload.zip.");
        }

        var tempFile = Path.GetTempFileName();
        using (var fileStream = File.Create(tempFile))
        {
            stream.CopyTo(fileStream);
        }

        ZipFile.ExtractToDirectory(tempFile, installDir, true);
        File.Delete(tempFile);
    }

    private static void WriteUninstallRegistry(string installDir, string uninstallPath, string appPath)
    {
        using var key = Registry.LocalMachine.CreateSubKey(UninstallKeyPath);
        if (key == null)
        {
            throw new InvalidOperationException("Unable to write uninstall information.");
        }

        key.SetValue("DisplayName", AppDisplayName);
        key.SetValue("DisplayVersion", "1.0.0");
        key.SetValue("Publisher", "NotebookAutoBrightness");
        key.SetValue("InstallLocation", installDir);
        key.SetValue("DisplayIcon", appPath);
        key.SetValue("UninstallString", $"\"{uninstallPath}\" /uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static void RemoveUninstallRegistry()
    {
        Registry.LocalMachine.DeleteSubKeyTree(UninstallKeyPath, false);
    }

    private static void RemoveAutoStart()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        key?.DeleteValue(AutoRunValueName, false);
    }

    private static void CloseRunningApp()
    {
        var processName = Path.GetFileNameWithoutExtension(AppExeName);
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                process.CloseMainWindow();
                if (!process.WaitForExit(2000))
                {
                    process.Kill(true);
                }
            }
            catch
            {
                // Ignore any process close failures.
            }
        }
    }

    private static void ScheduleSelfDelete(string selfPath)
    {
        if (string.IsNullOrWhiteSpace(selfPath) || !File.Exists(selfPath))
        {
            return;
        }

        var cmd = $"/c ping 127.0.0.1 -n 3 >nul & del \"{selfPath}\"";
        Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false });
    }
}
