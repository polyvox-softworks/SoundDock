using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace SoundDock.UI;

internal static class Theme
{
    internal static readonly Color Background = Color.FromArgb(35, 35, 35);
    internal static readonly Color Surface = Color.FromArgb(41, 41, 41);
    internal static readonly Color Hover = Color.FromArgb(57, 57, 57);
    internal static readonly Color Selected = Color.FromArgb(49, 53, 57);
    internal static readonly Color Border = Color.FromArgb(70, 70, 70);
    internal static readonly Color Text = Color.FromArgb(250, 250, 250);
    internal static readonly Color Muted = Color.FromArgb(178, 178, 178);
    // Windows stores the selected accent in AccentColorMenu as 0xAABBGGRR.
    // DWM colorization is a useful fallback, but is not always the user's
    // selected accent on Windows 11 (where taskbar colorization can be off).
    internal static Color Accent { get; private set; } = Color.FromArgb(0, 120, 215);
    internal static readonly Color Error = Color.FromArgb(255, 168, 158);

    internal static bool IsDarkTaskbar()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is not int value || value == 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            return true;
        }
    }

    internal static bool RefreshAccent()
    {
        Color next = ReadSystemAccent();
        if (next.ToArgb() == Accent.ToArgb()) return false;
        Accent = next;
        return true;
    }

    private static Color ReadSystemAccent()
    {
        if (SystemInformation.HighContrast) return SystemColors.Highlight;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            if (key?.GetValue("AccentColorMenu") is int colorValue)
            {
                uint raw = unchecked((uint)colorValue);
                // AccentColorMenu: 0xAABBGGRR (unlike DWM's 0xAARRGGBB).
                Color accent = Color.FromArgb((int)(raw & 0xFF),
                    (int)((raw >> 8) & 0xFF), (int)((raw >> 16) & 0xFF));
                if (accent.R + accent.G + accent.B > 24) return accent;
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            // Use the documented DWM fallback when personalization is restricted.
        }

        if (NativeMethods.DwmGetColorizationColor(out uint color, out _) >= 0)
            return Color.FromArgb((int)((color >> 16) & 0xFF),
                (int)((color >> 8) & 0xFF), (int)(color & 0xFF));

        return SystemColors.Highlight;
    }

    internal static GraphicsPath RoundedRectangle(RectangleF rectangle, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height));
        if (diameter <= 0) { path.AddRectangle(rectangle); return path; }
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// A filled, scalable taskbar speaker drawn in one native-resolution pass.
    /// The simpler silhouette and rounded 2-unit sound waves remain legible at
    /// 100%, 125%, 150% and 200% taskbar scaling without any stretched PNGs.
    /// </summary>
    internal static void DrawFluentSpeaker(Graphics graphics, RectangleF bounds,
        Color color, bool muted, int volumePercent)
    {
        var state = graphics.Save();
        try
        {
            graphics.TranslateTransform((float)Math.Round(bounds.X), (float)Math.Round(bounds.Y));
            graphics.ScaleTransform(bounds.Width / 24f, bounds.Height / 24f);
            using var brush = new SolidBrush(color);
            using var enclosure = RoundedRectangle(new RectangleF(2.5f, 9f, 5.2f, 6f), 1.2f);
            graphics.FillPath(brush, enclosure);
            graphics.FillPolygon(brush,
            [
                new PointF(6.6f, 9f), new PointF(11.7f, 4.4f),
                new PointF(12.6f, 4.7f), new PointF(12.6f, 19.3f),
                new PointF(11.7f, 19.6f), new PointF(6.6f, 15f)
            ]);
            using var pen = new Pen(color, 2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            if (muted || volumePercent <= 0)
            {
                // Keep the cross entirely inside the 24x24 view box: the
                // original outlined glyph could clip at small button sizes.
                graphics.DrawLine(pen, 16.1f, 9f, 21f, 15f);
                graphics.DrawLine(pen, 21f, 9f, 16.1f, 15f);
            }
            else
            {
                // Cubic waves are contained within the drawing box; wide
                // DrawArc curves extended beyond its right edge at 100% DPI.
                using var innerWave = new GraphicsPath();
                innerWave.AddBezier(new PointF(15.6f, 8.3f), new PointF(17.6f, 10f),
                    new PointF(17.6f, 14f), new PointF(15.6f, 15.7f));
                graphics.DrawPath(pen, innerWave);
                if (volumePercent > 45)
                {
                    using var outerWave = new GraphicsPath();
                    outerWave.AddBezier(new PointF(18.3f, 5.8f), new PointF(21.4f, 8.8f),
                        new PointF(21.4f, 15.2f), new PointF(18.3f, 18.2f));
                    graphics.DrawPath(pen, outerWave);
                }
            }
        }
        finally { graphics.Restore(state); }
    }

    internal static Button FlatButton(string text, string accessibleName)
    {
        var button = new Button
        {
            Text = text, AccessibleName = accessibleName, FlatStyle = FlatStyle.Flat,
            BackColor = Background, ForeColor = Muted, UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand, TabStop = true,
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Hover;
        button.FlatAppearance.MouseDownBackColor = Selected;
        return button;
    }
}

internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    internal DarkMenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Muted;
        base.OnRenderItemText(e);
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Background;
        public override Color MenuItemSelected => Theme.Hover;
        public override Color MenuItemBorder => Theme.Hover;
        public override Color MenuBorder => Theme.Border;
        public override Color ImageMarginGradientBegin => Theme.Background;
        public override Color ImageMarginGradientMiddle => Theme.Background;
        public override Color ImageMarginGradientEnd => Theme.Background;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Background;
        public override Color CheckBackground => Theme.Selected;
        public override Color CheckSelectedBackground => Theme.Selected;
        public override Color CheckPressedBackground => Theme.Selected;
    }
}
