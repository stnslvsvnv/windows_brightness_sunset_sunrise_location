using System.Drawing;

namespace NotebookAutoBrightness.Ui;

internal enum AppColorMode
{
    Light,
    Dark
}

internal enum SurfaceTone
{
    Standard,
    Muted,
    Hero
}

internal sealed class ThemePalette
{
    private ThemePalette(
        AppColorMode mode,
        Color windowBackground,
        Color windowBackgroundAlt,
        Color surface,
        Color surfaceMuted,
        Color surfaceRaised,
        Color border,
        Color textPrimary,
        Color textSecondary,
        Color textMuted,
        Color accent,
        Color accentStrong,
        Color accentSoft,
        Color track,
        Color toggleOff,
        Color inputBackground,
        Color inputBorder,
        Color heroStart,
        Color heroEnd,
        Color heroGlow,
        Color heroText,
        Color success,
        Color shadow,
        Color sliderKnob)
    {
        Mode = mode;
        WindowBackground = windowBackground;
        WindowBackgroundAlt = windowBackgroundAlt;
        Surface = surface;
        SurfaceMuted = surfaceMuted;
        SurfaceRaised = surfaceRaised;
        Border = border;
        TextPrimary = textPrimary;
        TextSecondary = textSecondary;
        TextMuted = textMuted;
        Accent = accent;
        AccentStrong = accentStrong;
        AccentSoft = accentSoft;
        Track = track;
        ToggleOff = toggleOff;
        InputBackground = inputBackground;
        InputBorder = inputBorder;
        HeroStart = heroStart;
        HeroEnd = heroEnd;
        HeroGlow = heroGlow;
        HeroText = heroText;
        Success = success;
        Shadow = shadow;
        SliderKnob = sliderKnob;
    }

    public AppColorMode Mode { get; }
    public Color WindowBackground { get; }
    public Color WindowBackgroundAlt { get; }
    public Color Surface { get; }
    public Color SurfaceMuted { get; }
    public Color SurfaceRaised { get; }
    public Color Border { get; }
    public Color TextPrimary { get; }
    public Color TextSecondary { get; }
    public Color TextMuted { get; }
    public Color Accent { get; }
    public Color AccentStrong { get; }
    public Color AccentSoft { get; }
    public Color Track { get; }
    public Color ToggleOff { get; }
    public Color InputBackground { get; }
    public Color InputBorder { get; }
    public Color HeroStart { get; }
    public Color HeroEnd { get; }
    public Color HeroGlow { get; }
    public Color HeroText { get; }
    public Color Success { get; }
    public Color Shadow { get; }
    public Color SliderKnob { get; }

    public static ThemePalette Create(AppColorMode mode) =>
        mode == AppColorMode.Dark ? CreateDark() : CreateLight();

    private static ThemePalette CreateLight() =>
        new(
            AppColorMode.Light,
            windowBackground: Color.FromArgb(247, 248, 250),
            windowBackgroundAlt: Color.FromArgb(242, 244, 247),
            surface: Color.FromArgb(255, 255, 255),
            surfaceMuted: Color.FromArgb(250, 251, 253),
            surfaceRaised: Color.FromArgb(255, 255, 255),
            border: Color.FromArgb(227, 232, 240),
            textPrimary: Color.FromArgb(17, 24, 39),
            textSecondary: Color.FromArgb(75, 85, 99),
            textMuted: Color.FromArgb(148, 163, 184),
            accent: Color.FromArgb(79, 110, 247),
            accentStrong: Color.FromArgb(53, 88, 246),
            accentSoft: Color.FromArgb(236, 241, 255),
            track: Color.FromArgb(229, 234, 242),
            toggleOff: Color.FromArgb(211, 218, 230),
            inputBackground: Color.FromArgb(255, 255, 255),
            inputBorder: Color.FromArgb(219, 226, 236),
            heroStart: Color.FromArgb(250, 251, 253),
            heroEnd: Color.FromArgb(245, 247, 250),
            heroGlow: Color.FromArgb(255, 255, 255),
            heroText: Color.FromArgb(17, 24, 39),
            success: Color.FromArgb(34, 197, 94),
            shadow: Color.FromArgb(12, 15, 23, 18),
            sliderKnob: Color.FromArgb(255, 255, 255));

    private static ThemePalette CreateDark() =>
        new(
            AppColorMode.Dark,
            windowBackground: Color.FromArgb(15, 17, 21),
            windowBackgroundAlt: Color.FromArgb(19, 22, 27),
            surface: Color.FromArgb(23, 26, 32),
            surfaceMuted: Color.FromArgb(19, 22, 27),
            surfaceRaised: Color.FromArgb(27, 32, 40),
            border: Color.FromArgb(39, 46, 56),
            textPrimary: Color.FromArgb(243, 244, 246),
            textSecondary: Color.FromArgb(176, 184, 197),
            textMuted: Color.FromArgb(123, 133, 151),
            accent: Color.FromArgb(124, 139, 255),
            accentStrong: Color.FromArgb(149, 161, 255),
            accentSoft: Color.FromArgb(29, 36, 64),
            track: Color.FromArgb(45, 51, 64),
            toggleOff: Color.FromArgb(65, 73, 90),
            inputBackground: Color.FromArgb(18, 22, 28),
            inputBorder: Color.FromArgb(43, 51, 64),
            heroStart: Color.FromArgb(19, 22, 27),
            heroEnd: Color.FromArgb(19, 22, 27),
            heroGlow: Color.FromArgb(255, 255, 255),
            heroText: Color.FromArgb(243, 244, 246),
            success: Color.FromArgb(52, 211, 153),
            shadow: Color.FromArgb(26, 0, 0, 0),
            sliderKnob: Color.FromArgb(250, 250, 252));
}
