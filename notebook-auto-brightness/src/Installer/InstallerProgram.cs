using System;
using System.IO;
using System.Windows.Forms;

namespace NotebookAutoBrightnessInstaller;

internal static class InstallerProgram
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (args.Length > 0 && args[0].Equals("/uninstall", StringComparison.OrdinalIgnoreCase))
        {
            InstallerOperations.UninstallInteractive();
            return;
        }

        if (args.Length > 0 && args[0].Equals("/uninstall-runner", StringComparison.OrdinalIgnoreCase))
        {
            var installDir = args.Length > 1 ? args[1] : string.Empty;
            var selfPath = args.Length > 2 ? args[2] : string.Empty;
            InstallerOperations.UninstallRunner(installDir, selfPath);
            return;
        }

        Application.Run(new InstallerForm());
    }
}
