using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NotebookAutoBrightness;

public static class WindowsThemeController
{
    private static readonly IntPtr HwndBroadcast = new(0xffff);
    private const string PersonalizeKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightTheme = "AppsUseLightTheme";
    private const string SystemUsesLightTheme = "SystemUsesLightTheme";
    private const uint WmSettingChange = 0x001A;
    private const uint WmThemeChanged = 0x031A;
    private const uint SmtoAbortIfHung = 0x0002;

    private static ThemeMode? _lastAppliedTheme;

    public enum ThemeMode
    {
        Light = 1,
        Dark = 0
    }

    public static bool SetTheme(ThemeMode mode)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey, true);
            if (key == null)
            {
                return false;
            }

            var value = (int)mode;
            key.SetValue(AppsUseLightTheme, value, RegistryValueKind.DWord);
            key.SetValue(SystemUsesLightTheme, value, RegistryValueKind.DWord);
            BroadcastThemeChange();
            _lastAppliedTheme = mode;
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Switches the theme only when the system really differs and reports whether a switch (and its
    // system-wide broadcast) happened. Both registry values are checked: Windows lets the app mode
    // and the system mode differ ("Custom" mode), so trusting only the apps value would leave the
    // system theme dark while thinking everything is fine. An unreadable value is still not a
    // reason to broadcast on every tick - once we set exactly this theme, it counts as applied.
    public static bool EnsureTheme(ThemeMode mode)
    {
        var apps = GetCurrentTheme();
        var system = GetSystemTheme();
        if (apps == mode && system == mode)
        {
            return false;
        }

        if (_lastAppliedTheme == mode && (apps == mode || apps == null))
        {
            return false;
        }

        return SetTheme(mode);
    }

    public static bool SetLightTheme() => SetTheme(ThemeMode.Light);

    public static bool SetDarkTheme() => SetTheme(ThemeMode.Dark);

    public static ThemeMode? GetCurrentTheme() => ReadTheme(AppsUseLightTheme);

    public static ThemeMode? GetSystemTheme() => ReadTheme(SystemUsesLightTheme);

    private static ThemeMode? ReadTheme(string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, false);
            if (key?.GetValue(valueName) is int value)
            {
                return (ThemeMode)value;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static void BroadcastThemeChange()
    {
        _ = SendMessageTimeout(
            HwndBroadcast,
            WmSettingChange,
            UIntPtr.Zero,
            "ImmersiveColorSet",
            SmtoAbortIfHung,
            1000,
            out _);

        _ = PostMessage(HwndBroadcast, WmThemeChanged, UIntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        UIntPtr wParam,
        string? lParam,
        uint flags,
        uint timeout,
        out UIntPtr result);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);
}
