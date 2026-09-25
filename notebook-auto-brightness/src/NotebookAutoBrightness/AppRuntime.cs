using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace NotebookAutoBrightness;

internal static class AppRuntime
{
    public const string AppName = "Notebook sunrise/sunset auto brightness";
    public const string AutoRunValueName = "NotebookSunriseSunsetAutoBrightness";
    public const string BackgroundArgument = "--background";

    // Stamped by the project file from the build date as 1.0.<day>.<month>, so the window title, the
    // installed programs list and the log all say which build is actually running.
    public static string BuildVersion { get; } = ReadBuildVersion();

    private static string ReadBuildVersion()
    {
        var informational = typeof(AppRuntime).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return "unknown";
        }

        var separator = informational.IndexOf('+');
        return separator < 0 ? informational : informational[..separator];
    }

    public static string BuildAutoStartCommand(string executablePath) =>
        $"\"{executablePath}\" {BackgroundArgument}";

    public static Icon LoadAppIcon()
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
