using System.Drawing.Drawing2D;

namespace SoundDock.UI;

/// <summary>A neutral, rounded footer action with keyboard focus and Windows DPI support.</summary>
internal sealed class RoundedActionButton : Button
{
    private bool _hovered;
    public bool ShowExternalArrow { get; set; }
    public bool ShowSettingsIcon { get; set; }
    /// <summary>Draw a compact down chevron for actions that open a menu.</summary>
    public bool ShowDropdownChevron { get; set; }

    public RoundedActionButton(string text, string accessibleName)
    {
        Text = text;
        AccessibleName = accessibleName;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f;
        using var shape = Theme.RoundedRectangle(
            new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 7f * scale);
        using var fill = new SolidBrush(_hovered || Focused ? Color.FromArgb(59, 59, 59) : Theme.Surface);
        using var outline = new Pen(Focused ? Theme.Accent : Theme.Border, Math.Max(1f, scale));
        g.FillPath(fill, shape);
        g.DrawPath(outline, shape);

        int pad = (int)Math.Round(12 * scale);
        int reserved = ShowExternalArrow || ShowDropdownChevron ? (int)Math.Round(25 * scale) : 0;
        int leading = ShowSettingsIcon ? (int)Math.Round(25 * scale) : 0;
        TextRenderer.DrawText(g, Text, Font,
            new Rectangle(pad + leading, 0, Math.Max(1, Width - pad * 2 - reserved - leading), Height),
            Enabled ? Theme.Text : Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

        if (ShowSettingsIcon)
        {
            float cx = pad + 7.8f * scale;
            float cy = Height / 2f;
            using var pen = new Pen(Theme.Muted, 1.3f * scale)
            { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawEllipse(pen, cx - 4.3f * scale, cy - 4.3f * scale,
                8.6f * scale, 8.6f * scale);
            g.DrawEllipse(pen, cx - 1.4f * scale, cy - 1.4f * scale,
                2.8f * scale, 2.8f * scale);
            for (int i = 0; i < 8; i++)
            {
                double angle = i * Math.PI / 4d;
                float dx = (float)Math.Cos(angle);
                float dy = (float)Math.Sin(angle);
                g.DrawLine(pen, cx + 5.8f * scale * dx, cy + 5.8f * scale * dy,
                    cx + 7f * scale * dx, cy + 7f * scale * dy);
            }
        }

        if (ShowExternalArrow)
        {
            float x = Width - pad - reserved / 2f;
            float y = Height / 2f;
            using var pen = new Pen(Theme.Muted, 1.4f * scale)
            { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLine(pen, x - 5f * scale, y, x + 5f * scale, y);
            g.DrawLines(pen, [new PointF(x + 1.5f * scale, y - 3.5f * scale),
                new PointF(x + 5f * scale, y), new PointF(x + 1.5f * scale, y + 3.5f * scale)]);
        }
        else if (ShowDropdownChevron)
        {
            float cx = Width - pad - reserved / 2f;
            float cy = Height / 2f;
            float dx = 3.5f * scale;
            float dy = 2f * scale;
            using var pen = new Pen(Theme.Muted, 1.4f * scale)
            { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, [new PointF(cx - dx, cy - dy),
                new PointF(cx, cy + dy), new PointF(cx + dx, cy - dy)]);
        }
    }
}

/// <summary>Draws an X centered geometrically, without baseline drift from a text glyph.</summary>
internal sealed class CenteredCloseButton : Button
{
    private bool _hovered;

    public CenteredCloseButton()
    {
        AccessibleName = "Close sound panel";
        AccessibleRole = AccessibleRole.PushButton;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Theme.Background;
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f;
        if (_hovered || Focused)
        {
            using var fill = new SolidBrush(Theme.Hover);
            using var path = Theme.RoundedRectangle(
                new RectangleF(0, 0, Width - 1, Height - 1), 7f * scale);
            g.FillPath(fill, path);
        }
        float cx = Width / 2f, cy = Height / 2f;
        float offset = 4.6f * scale;
        using var pen = new Pen(Focused ? Theme.Text : Theme.Muted, Math.Max(1.5f, 1.65f * scale))
        {
            StartCap = LineCap.Round, EndCap = LineCap.Round
        };
        g.DrawLine(pen, cx - offset, cy - offset, cx + offset, cy + offset);
        g.DrawLine(pen, cx + offset, cy - offset, cx - offset, cy + offset);
    }
}

internal sealed class RoundedGroupPanel : Panel
{
    public RoundedGroupPanel()
    {
        BackColor = Theme.Background;
        DoubleBuffered = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var fill = new SolidBrush(Theme.Surface);
        using var border = new Pen(Theme.Border);
        using var shape = Theme.RoundedRectangle(
            new RectangleF(.5f, .5f, Width - 1f, Height - 1f), 7f * DeviceDpi / 96f);
        e.Graphics.FillPath(fill, shape);
        e.Graphics.DrawPath(border, shape);
    }
}

internal sealed class HiddenRestoreButton : Button
{
    private bool _hovered;
    private readonly Font _textFont = new("Segoe UI", 8.5f);

    public HiddenRestoreButton(string text)
    {
        Text = text;
        AccessibleName = text;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Theme.Surface;
        Font = _textFont;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f;
        using var fill = new SolidBrush(_hovered || Focused ? Theme.Hover : Color.FromArgb(55, 55, 55));
        using var shape = Theme.RoundedRectangle(
            new RectangleF(0, 0, Width - 1, Height - 1), 5f * scale);
        g.FillPath(fill, shape);
        TextRenderer.DrawText(g, Text, Font,
            new Rectangle((int)Math.Round(10 * scale), 0,
                Math.Max(1, Width - (int)Math.Round(20 * scale)), Height), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(g, ClientRectangle, Theme.Text, Theme.Surface);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _textFont.Dispose();
    }
}
