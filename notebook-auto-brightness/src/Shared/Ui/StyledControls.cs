using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NotebookAutoBrightness.Ui;

internal enum ButtonVisualKind
{
    Primary,
    Secondary
}

internal sealed class ThemedCardPanel : Panel, IThemeAware
{
    private ThemePalette _palette = ThemePalette.Create(AppColorMode.Light);

    public ThemedCardPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);

        BackColor = Color.Transparent;
        Margin = new Padding(0, 0, 0, 10);
        Padding = new Padding(14);
    }

    public SurfaceTone Tone { get; set; } = SurfaceTone.Standard;

    public void ApplyTheme(ThemePalette palette)
    {
        _palette = palette;
        Invalidate();
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        UpdateRegion();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = Rectangle.Inflate(ClientRectangle, -2, -2);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using var path = UiDrawing.CreateRoundedRectangle(bounds, 12);

        if (Tone == SurfaceTone.Hero)
        {
            using var heroBrush = new LinearGradientBrush(bounds, _palette.HeroStart, _palette.HeroEnd, 90f);
            e.Graphics.FillPath(heroBrush, path);
            using var borderPen = new Pen(_palette.Border, 1f);
            e.Graphics.DrawPath(borderPen, path);
            return;
        }

        var fillColor = Tone == SurfaceTone.Muted ? _palette.SurfaceMuted : _palette.Surface;
        using var fillBrush = new SolidBrush(fillColor);
        e.Graphics.FillPath(fillBrush, path);

        using var pen = new Pen(_palette.Border, 1f);
        e.Graphics.DrawPath(pen, path);
    }

    private void UpdateRegion()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        using var path = UiDrawing.CreateRoundedRectangle(new Rectangle(0, 0, Width, Height), 12);
        Region = new Region(path);
    }
}

internal sealed class ThemedButton : Button, IThemeAware
{
    private ThemePalette _palette = ThemePalette.Create(AppColorMode.Light);
    private bool _hovered;
    private bool _pressed;

    public ThemedButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Height = 34;
        Width = 144;
        Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point);
    }

    public ButtonVisualKind VisualKind { get; set; } = ButtonVisualKind.Primary;

    public void ApplyTheme(ThemePalette palette)
    {
        _palette = palette;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = Rectangle.Inflate(ClientRectangle, -1, -1);
        using var path = UiDrawing.CreateRoundedRectangle(bounds, 10);

        using var brush = new SolidBrush(ResolveBackground());
        using var pen = new Pen(ResolveBorder(), 1.1f);
        pevent.Graphics.FillPath(brush, path);
        pevent.Graphics.DrawPath(pen, path);

        if (Focused)
        {
            using var focusPen = new Pen(Color.FromArgb(120, _palette.AccentStrong), 1.3f);
            var focusBounds = Rectangle.Inflate(bounds, -3, -3);
            using var focusPath = UiDrawing.CreateRoundedRectangle(focusBounds, 8);
            pevent.Graphics.DrawPath(focusPen, focusPath);
        }

        TextRenderer.DrawText(
            pevent.Graphics,
            Text,
            Font,
            bounds,
            ResolveTextColor(),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private Color ResolveBackground()
    {
        if (!Enabled)
        {
            return _palette.Track;
        }

        if (VisualKind == ButtonVisualKind.Secondary)
        {
            return _pressed
                ? _palette.SurfaceMuted
                : _hovered ? _palette.SurfaceRaised : _palette.Surface;
        }

        if (_palette.Mode == AppColorMode.Dark)
        {
            if (_pressed)
            {
                return Color.FromArgb(222, 228, 238);
            }

            return _hovered ? Color.FromArgb(247, 249, 252) : Color.FromArgb(235, 239, 245);
        }

        if (_pressed)
        {
            return Color.FromArgb(10, 15, 24);
        }

        return _hovered ? Color.FromArgb(28, 36, 52) : Color.FromArgb(17, 24, 39);
    }

    private Color ResolveBorder()
    {
        if (VisualKind == ButtonVisualKind.Secondary)
        {
            return _hovered ? _palette.Accent : _palette.Border;
        }

        return VisualKind == ButtonVisualKind.Primary
            ? Color.FromArgb(0, 0, 0, 0)
            : _palette.Border;
    }

    private Color ResolveTextColor() =>
        VisualKind == ButtonVisualKind.Secondary
            ? _palette.TextPrimary
            : _palette.Mode == AppColorMode.Dark ? Color.FromArgb(15, 17, 21) : Color.White;
}

internal sealed class SettingToggleRow : Control, IThemeAware
{
    private ThemePalette _palette = ThemePalette.Create(AppColorMode.Light);
    private bool _checked;
    private bool _hovered;

    public SettingToggleRow()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.Selectable,
            true);

        Cursor = Cursors.Hand;
        Height = 42;
        Width = 280;
        Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point);
        TitleFont = new Font("Segoe UI Semibold", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
    }

    public event EventHandler? CheckedChanged;

    public string Description { get; set; } = string.Empty;
    public Font TitleFont { get; }
    public SurfaceTone Tone { get; set; } = SurfaceTone.Standard;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }

            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ApplyTheme(ThemePalette palette)
    {
        _palette = palette;
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (Enabled)
        {
            Checked = !Checked;
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            Checked = !Checked;
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var backgroundBrush = new SolidBrush(ResolveSurfaceColor()))
        {
            e.Graphics.FillRectangle(backgroundBrush, ClientRectangle);
        }

        var textBounds = new Rectangle(0, 0, Width - 72, Height - 1);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            TitleFont,
            new Rectangle(textBounds.X, textBounds.Y, textBounds.Width, 18),
            Enabled ? _palette.TextPrimary : _palette.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(
            e.Graphics,
            Description,
            Font,
            new Rectangle(textBounds.X, textBounds.Y + 15, textBounds.Width, 18),
            _palette.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);

        var toggleBounds = new Rectangle(Width - 48, (Height - 22) / 2, 38, 22);
        using var togglePath = UiDrawing.CreateRoundedRectangle(toggleBounds, 11);
        var trackColor = !Enabled
            ? _palette.Track
            : Checked ? _palette.Accent : (_hovered ? _palette.Track : _palette.ToggleOff);
        using var trackBrush = new SolidBrush(trackColor);
        e.Graphics.FillPath(trackBrush, togglePath);

        var thumbSize = 14;
        var thumbX = Checked ? toggleBounds.Right - thumbSize - 4 : toggleBounds.Left + 4;
        var thumbBounds = new Rectangle(thumbX, toggleBounds.Top + 4, thumbSize, thumbSize);
        using var thumbBrush = new SolidBrush(_palette.SliderKnob);
        e.Graphics.FillEllipse(thumbBrush, thumbBounds);

        using var separatorPen = new Pen(Color.FromArgb(_palette.Mode == AppColorMode.Dark ? 36 : 22, _palette.Border), 1f);
        e.Graphics.DrawLine(separatorPen, 0, Height - 1, Width, Height - 1);

        if (Focused)
        {
            using var focusPen = new Pen(Color.FromArgb(120, _palette.AccentStrong), 1.2f);
            var focusBounds = Rectangle.Inflate(toggleBounds, 5, 5);
            using var focusPath = UiDrawing.CreateRoundedRectangle(focusBounds, 15);
            e.Graphics.DrawPath(focusPen, focusPath);
        }
    }

    private Color ResolveSurfaceColor() =>
        Tone == SurfaceTone.Muted ? _palette.SurfaceMuted : _palette.Surface;
}

internal sealed class MetricSlider : Control, IThemeAware
{
    private ThemePalette _palette = ThemePalette.Create(AppColorMode.Light);
    private bool _dragging;
    private int _minimum;
    private int _maximum = 100;
    private int _value;

    public MetricSlider()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.Selectable,
            true);

        Height = 54;
        Width = 280;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point);
        TitleFont = new Font("Segoe UI Semibold", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        SmallChange = 1;
        LargeChange = 5;
        ValueFormatter = static value => value.ToString();
    }

    public event EventHandler? ValueChanged;

    public string Description { get; set; } = string.Empty;
    public Font TitleFont { get; }
    public SurfaceTone Tone { get; set; } = SurfaceTone.Standard;
    public Func<int, string> ValueFormatter { get; set; }

    public int Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            if (_maximum < _minimum)
            {
                _maximum = _minimum;
            }

            Value = Math.Clamp(Value, _minimum, _maximum);
        }
    }

    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(value, _minimum);
            Value = Math.Clamp(Value, _minimum, _maximum);
        }
    }

    public int SmallChange { get; set; }
    public int LargeChange { get; set; }

    public int Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, Minimum, Maximum);
            if (_value == clamped)
            {
                return;
            }

            _value = clamped;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ApplyTheme(ThemePalette palette)
    {
        _palette = palette;
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.PageDown or Keys.PageUp || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Left:
            case Keys.Down:
                Value -= SmallChange;
                e.Handled = true;
                return;
            case Keys.Right:
            case Keys.Up:
                Value += SmallChange;
                e.Handled = true;
                return;
            case Keys.PageDown:
                Value -= LargeChange;
                e.Handled = true;
                return;
            case Keys.PageUp:
                Value += LargeChange;
                e.Handled = true;
                return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        _dragging = true;
        UpdateValueFromX(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            UpdateValueFromX(e.X);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        base.OnMouseUp(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        Value += e.Delta > 0 ? SmallChange : -SmallChange;
        base.OnMouseWheel(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var backgroundBrush = new SolidBrush(ResolveSurfaceColor()))
        {
            e.Graphics.FillRectangle(backgroundBrush, ClientRectangle);
        }

        var valueText = ValueFormatter(Value);
        var titleWidth = Math.Max(0, Width - 82);

        TextRenderer.DrawText(
            e.Graphics,
            Text,
            TitleFont,
            new Rectangle(0, 0, titleWidth, 18),
            Enabled ? _palette.TextPrimary : _palette.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(
            e.Graphics,
            Description,
            Font,
            new Rectangle(0, 16, Width - 6, 16),
            _palette.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);

        var pillBounds = new Rectangle(Width - 60, 0, 56, 22);
        using (var pillPath = UiDrawing.CreateRoundedRectangle(pillBounds, 11))
        using (var pillBrush = new SolidBrush(_palette.SurfaceMuted))
        using (var pillPen = new Pen(_palette.Border, 1f))
        {
            e.Graphics.FillPath(pillBrush, pillPath);
            e.Graphics.DrawPath(pillPen, pillPath);
        }

        using (var valueFont = new Font("Segoe UI Semibold", 8.5F, FontStyle.Regular, GraphicsUnit.Point))
        {
            TextRenderer.DrawText(
                e.Graphics,
                valueText,
                valueFont,
                pillBounds,
                _palette.TextPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        var trackBounds = GetTrackBounds();
        using (var trackPath = UiDrawing.CreateRoundedRectangle(trackBounds, 3))
        using (var trackBrush = new SolidBrush(_palette.Track))
        {
            e.Graphics.FillPath(trackBrush, trackPath);
        }

        var progressWidth = Math.Max(0, ValueToTrackX(Value) - trackBounds.Left);
        if (progressWidth > 0)
        {
            var progressBounds = new Rectangle(trackBounds.Left, trackBounds.Top, progressWidth, trackBounds.Height);
            using var progressPath = UiDrawing.CreateRoundedRectangle(progressBounds, 3);
            using var progressBrush = new SolidBrush(_palette.Accent);
            e.Graphics.FillPath(progressBrush, progressPath);
        }

        var knobX = ValueToTrackX(Value);
        var knobBounds = new Rectangle(knobX - 6, trackBounds.Top - 4, 12, 12);

        using (var knobBrush = new SolidBrush(_palette.SliderKnob))
        using (var borderPen = new Pen(_palette.AccentStrong, 1.1f))
        {
            e.Graphics.FillEllipse(knobBrush, knobBounds);
            e.Graphics.DrawEllipse(borderPen, knobBounds);
        }

        if (Focused)
        {
            using var focusPen = new Pen(Color.FromArgb(120, _palette.AccentStrong), 1.2f);
            e.Graphics.DrawEllipse(focusPen, knobBounds.Left - 4, knobBounds.Top - 4, knobBounds.Width + 8, knobBounds.Height + 8);
        }

        using var separatorPen = new Pen(Color.FromArgb(_palette.Mode == AppColorMode.Dark ? 36 : 22, _palette.Border), 1f);
        e.Graphics.DrawLine(separatorPen, 0, Height - 1, Width, Height - 1);
    }

    private void UpdateValueFromX(int x)
    {
        if (Maximum == Minimum)
        {
            Value = Minimum;
            return;
        }

        var trackBounds = GetTrackBounds();
        var clampedX = Math.Max(trackBounds.Left, Math.Min(trackBounds.Right, x));
        var ratio = (double)(clampedX - trackBounds.Left) / Math.Max(1, trackBounds.Width);
        Value = Minimum + (int)Math.Round((Maximum - Minimum) * ratio);
    }

    private Rectangle GetTrackBounds() => new(0, Height - 9, Width - 14, 4);

    private int ValueToTrackX(int value)
    {
        var trackBounds = GetTrackBounds();
        if (Maximum == Minimum)
        {
            return trackBounds.Left;
        }

        var ratio = (double)(value - Minimum) / (Maximum - Minimum);
        return trackBounds.Left + (int)Math.Round(trackBounds.Width * ratio);
    }

    private Color ResolveSurfaceColor() =>
        Tone == SurfaceTone.Muted ? _palette.SurfaceMuted : _palette.Surface;
}

internal static class UiDrawing
{
    public static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = Math.Max(1, radius * 2);
        var path = new GraphicsPath();

        if (bounds.Width <= diameter || bounds.Height <= diameter)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.StartFigure();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
