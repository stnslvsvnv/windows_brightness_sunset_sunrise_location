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

        // Keep a broken UI-thread callback from turning into a modal dialog or a dead tray app:
        // the exception is logged and the schedule keeps running.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => AppLog.WriteError("ui thread", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.WriteError(
            "unhandled",
            e.ExceptionObject as Exception ?? new InvalidOperationException("unknown failure"));

        AppLog.Write($"{AppRuntime.AppName} {AppRuntime.BuildVersion} started (pid {Environment.ProcessId})");

        var showSettingsOnStart = !args.Any(static arg =>
            string.Equals(arg, AppRuntime.BackgroundArgument, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "/background", StringComparison.OrdinalIgnoreCase));

        Application.Run(new TrayApplicationContext(showSettingsOnStart));
    }
}
