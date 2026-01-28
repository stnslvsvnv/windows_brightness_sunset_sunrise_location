using System;
using System.Management;

namespace NotebookAutoBrightness;

public static class BrightnessController
{
    public static bool TrySetBrightness(int percent, out string? error)
    {
        error = null;

        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject obj in searcher.Get())
            {
                obj.InvokeMethod("WmiSetBrightness", new object[] { 1, percent });
                return true;
            }

            error = "No compatible monitor was found.";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
