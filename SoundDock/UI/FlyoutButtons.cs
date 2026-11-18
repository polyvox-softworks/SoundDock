using System.Drawing.Drawing2D;

namespace SoundDock.UI;

/// <summary>A neutral, rounded footer action with keyboard focus and Windows DPI support.</summary>
internal sealed class RoundedActionButton : Button
{
    private bool _hovered;
    public bool ShowExternalArrow { get; set; }
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
            new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 8f * scale);
        using var fill = new SolidBrush(_hovered || Focused ? Theme.Hover : Theme.Surface);
        using var outline = new Pen(Focused ? Theme.Accent : Theme.Border, Math.Max(1f, scale));
        g.FillPath(fill, shape);
        g.DrawPath(outline, shape);
        int pad = (int)Math.Round(12 * scale);
        int reserved = ShowExternalArrow || ShowDropdownChevron ? (int)Math.Round(28 * scale) : 0;
        TextRenderer.DrawText(g, Text, Font,
            new Rectangle(pad, 0, Math.Max(1, Width - pad * 2 - reserved), Height),
            Enabled ? Theme.Text : Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        if (ShowExternalArrow)
        {
            TextRenderer.DrawText(g, "↗", Font,
                new Rectangle(Width - pad - reserved, 0, reserved, Height), Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
        else if (ShowDropdownChevron)
        {
            // Geometric chevron stays centered and crisp at fractional display scaling.
            float cx = Width - pad - reserved / 2f;
            float cy = Height / 2f;
            float dx = 3.6f * scale;
            float dy = 2.2f * scale;
            using var chevronPen = new Pen(Theme.Muted, Math.Max(1.5f, 1.5f * scale))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };
            g.DrawLines(chevronPen,
            [
                new PointF(cx - dx, cy - dy),
                new PointF(cx, cy + dy),
                new PointF(cx + dx, cy - dy),
            ]);
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
