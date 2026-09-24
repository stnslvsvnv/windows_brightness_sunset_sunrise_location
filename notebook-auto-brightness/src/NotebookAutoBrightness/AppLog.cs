using System;
using System.IO;
using System.Text;

namespace NotebookAutoBrightness;

// Minimal rolling log next to the settings file. It exists because the app has no other way to
// explain a stalled schedule: a wedged apply, a missed wake-up or a theme switch all look the same
// from the outside. Logging never throws and never blocks the schedule.
internal static class AppLog
{
    private const long MaxBytes = 100 * 1024;

    private static readonly object Gate = new();
    private static bool _disabled;

    public static string LogPath => Path.Combine(SettingsStore.SettingsDirectory, "log.txt");

    public static void Write(string message)
    {
        if (_disabled)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(SettingsStore.SettingsDirectory);
                Rotate();
                File.AppendAllText(
                    LogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // A broken log must not take the app down with it.
            _disabled = true;
        }
    }

    public static void WriteError(string context, Exception exception)
    {
        Write($"ERROR {context}: {exception.GetType().Name}: {exception.Message}");
    }

    private static void Rotate()
    {
        var file = new FileInfo(LogPath);
        if (!file.Exists || file.Length < MaxBytes)
        {
            return;
        }

        var previous = LogPath + ".1";
        File.Delete(previous);
        File.Move(LogPath, previous);
    }
}
