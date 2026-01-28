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

    public static string Install(string installDir)
    {
        Directory.CreateDirectory(installDir);
        ExtractPayload(installDir);

        var installerPath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        var uninstallPath = Path.Combine(installDir, UninstallExeName);
        if (!string.IsNullOrWhiteSpace(installerPath))
        {
            File.Copy(installerPath, uninstallPath, true);
        }

        var appPath = Path.Combine(installDir, AppExeName);
        WriteUninstallRegistry(installDir, uninstallPath, appPath);
        return appPath;
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

        var appPath = Path.Combine(installDir, AppExeName);
        if (Directory.Exists(installDir) && File.Exists(appPath))
        {
            Directory.Delete(installDir, true);
        }

        RemoveUninstallRegistry();
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
