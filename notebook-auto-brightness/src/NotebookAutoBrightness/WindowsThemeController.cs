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

    // Switches the theme only when it really differs and reports whether a switch (and its
    // system-wide broadcast) happened. An unreadable registry value is not a reason to broadcast
    // again: once we have set the theme ourselves, that value is treated as the current one.
    public static bool EnsureTheme(ThemeMode mode)
    {
        var current = GetCurrentTheme();
        if (current == mode || (current == null && _lastAppliedTheme == mode))
        {
            return false;
        }

        return SetTheme(mode);
    }

    public static bool SetLightTheme() => SetTheme(ThemeMode.Light);

    public static bool SetDarkTheme() => SetTheme(ThemeMode.Dark);

    public static ThemeMode? GetCurrentTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, false);
            if (key == null)
            {
                return null;
            }

            var value = key.GetValue(AppsUseLightTheme);
            if (value is int intValue)
            {
                return (ThemeMode)intValue;
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
