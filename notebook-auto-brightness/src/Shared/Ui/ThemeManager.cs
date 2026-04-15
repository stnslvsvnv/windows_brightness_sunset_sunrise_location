using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NotebookAutoBrightness.Ui;

internal interface IThemeAware
{
    void ApplyTheme(ThemePalette palette);
}

internal static class ThemeManager
{
    private const string PersonalizeKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightTheme = "AppsUseLightTheme";
    private const int DwmaUseImmersiveDarkMode = 20;

    public static AppColorMode GetSystemMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, false);
            var value = key?.GetValue(AppsUseLightTheme);
            return value is int intValue && intValue == 0
                ? AppColorMode.Dark
                : AppColorMode.Light;
        }
        catch
        {
            return AppColorMode.Light;
        }
    }

    public static ThemePalette CreatePalette() => ThemePalette.Create(GetSystemMode());

    public static bool IsThemeRelatedChange(UserPreferenceCategory category) =>
        category is UserPreferenceCategory.Color or UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle;

    public static void ApplyTheme(Control root, ThemePalette palette)
    {
        ApplyCore(root, palette);

        foreach (Control child in root.Controls)
        {
            ApplyTheme(child, palette);
        }

        if (root is Form form)
        {
            ApplyWindowChrome(form, palette);
        }
    }

    public static void ApplyTheme(ContextMenuStrip menu, ThemePalette palette)
    {
        menu.Renderer = new SolarToolStripRenderer(palette);
        menu.ShowImageMargin = false;
        menu.BackColor = palette.SurfaceRaised;
        menu.ForeColor = palette.TextPrimary;

        foreach (ToolStripItem item in menu.Items)
        {
            item.BackColor = palette.SurfaceRaised;
            item.ForeColor = palette.TextPrimary;
        }
    }

    private static void ApplyCore(Control control, ThemePalette palette)
    {
        if (control is IThemeAware themeAware)
        {
            themeAware.ApplyTheme(palette);
            return;
        }

        switch (control)
        {
            case Form form:
                form.BackColor = palette.WindowBackground;
                form.ForeColor = palette.TextPrimary;
                break;
            case TableLayoutPanel:
            case FlowLayoutPanel:
                control.BackColor = Color.Transparent;
                control.ForeColor = palette.TextPrimary;
                break;
            case Label label:
                label.BackColor = Color.Transparent;
                break;
            case TextBox textBox:
                textBox.BackColor = palette.InputBackground;
                textBox.ForeColor = palette.TextPrimary;
                textBox.BorderStyle = BorderStyle.FixedSingle;
                break;
            case DateTimePicker picker:
                picker.CalendarMonthBackground = palette.SurfaceRaised;
                picker.CalendarForeColor = palette.TextPrimary;
                picker.CalendarTitleBackColor = palette.SurfaceMuted;
                picker.CalendarTitleForeColor = palette.TextPrimary;
                picker.CalendarTrailingForeColor = palette.TextMuted;
                break;
            case ProgressBar progressBar:
                progressBar.ForeColor = palette.Accent;
                progressBar.BackColor = palette.Track;
                break;
        }
    }

    private static void ApplyWindowChrome(Form form, ThemePalette palette)
    {
        if (!form.IsHandleCreated)
        {
            return;
        }

        try
        {
            var useDarkFrame = palette.Mode == AppColorMode.Dark ? 1 : 0;
            _ = DwmSetWindowAttribute(form.Handle, DwmaUseImmersiveDarkMode, ref useDarkFrame, sizeof(int));
        }
        catch
        {
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
}

internal sealed class SolarToolStripRenderer : ToolStripProfessionalRenderer
{
    public SolarToolStripRenderer(ThemePalette palette)
        : base(new SolarColorTable(palette))
    {
    }
}

internal sealed class SolarColorTable : ProfessionalColorTable
{
    private readonly ThemePalette _palette;

    public SolarColorTable(ThemePalette palette)
    {
        _palette = palette;
        UseSystemColors = false;
    }

    public override Color MenuBorder => _palette.Border;
    public override Color MenuItemBorder => _palette.Border;
    public override Color MenuItemSelected => _palette.SurfaceMuted;
    public override Color MenuItemSelectedGradientBegin => _palette.SurfaceMuted;
    public override Color MenuItemSelectedGradientEnd => _palette.SurfaceMuted;
    public override Color MenuItemPressedGradientBegin => _palette.Surface;
    public override Color MenuItemPressedGradientMiddle => _palette.Surface;
    public override Color MenuItemPressedGradientEnd => _palette.Surface;
    public override Color ToolStripDropDownBackground => _palette.SurfaceRaised;
    public override Color ImageMarginGradientBegin => _palette.SurfaceRaised;
    public override Color ImageMarginGradientMiddle => _palette.SurfaceRaised;
    public override Color ImageMarginGradientEnd => _palette.SurfaceRaised;
}
