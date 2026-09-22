using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace NotebookAutoBrightness;

internal static class AppRuntime
{
    public const string AppName = "Notebook sunrise/sunset auto brightness";
    public const string AutoRunValueName = "NotebookSunriseSunsetAutoBrightness";
    public const string BackgroundArgument = "--background";

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
