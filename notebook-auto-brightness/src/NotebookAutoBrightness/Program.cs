using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace NotebookAutoBrightness;

internal static class Program
{
    private const string SingleInstanceMutexName = @"Local\NotebookSunriseSunsetAutoBrightness.SingleInstance";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            return;
        }

        ApplicationConfiguration.Initialize();

        var showSettingsOnStart = !args.Any(static arg =>
            string.Equals(arg, AppRuntime.BackgroundArgument, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "/background", StringComparison.OrdinalIgnoreCase));

        Application.Run(new TrayApplicationContext(showSettingsOnStart));
    }
}
