using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SoundDock.UI;

/// <summary>
/// Renders small, native-size GDI text into a grayscale coverage mask, then
/// blends that mask into a premultiplied-alpha layered-window bitmap.
///
/// GDI+ DrawString with an antialiased font on a transparent ARGB surface can
/// look soft at taskbar sizes, and ClearType RGB fringes cannot be blended
/// correctly onto a taskbar whose actual background is outside our control.
/// This retains the hinting of native Windows text while using grayscale alpha.
/// The result is always drawn 1:1 at the HWND's current physical pixel size.
/// </summary>
internal static class LayeredTextRenderer
{
    internal static void Draw(Bitmap target, string text, Font font, Rectangle bounds, Color color)
    {
        if (string.IsNullOrEmpty(text) || bounds.Width <= 0 || bounds.Height <= 0) return;

        var clipped = Rectangle.Intersect(bounds, new Rectangle(Point.Empty, target.Size));
        if (clipped.IsEmpty) return;

        // TextRenderer uses GDI's Windows font hinter; a neutral background
        // makes it possible to extract a coverage mask with no colored fringe.
        using var mask = new Bitmap(clipped.Width, clipped.Height, PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(mask))
        {
            graphics.Clear(Color.Black);
            TextRenderer.DrawText(graphics, text, font,
                new Rectangle(bounds.X - clipped.X, bounds.Y - clipped.Y, bounds.Width, bounds.Height),
                Color.White, Color.Black,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis |
                TextFormatFlags.VerticalCenter);
        }

        // Blend coverage into PArgb instead of copying GDI's opaque text pixels.
        // Both buffers are small (at most ~200x42), and drawing only occurs when
        // the volume/label/hover state changes, not on every polling tick.
        BitmapData source = mask.LockBits(new Rectangle(Point.Empty, mask.Size),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        BitmapData? destination = null;
        try
        {
            destination = target.LockBits(clipped, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
            int rowBytes = checked(clipped.Width * 4);
            var src = new byte[rowBytes];
            var dst = new byte[rowBytes];
            for (int y = 0; y < clipped.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(source.Scan0, y * source.Stride), src, 0, rowBytes);
                Marshal.Copy(IntPtr.Add(destination.Scan0, y * destination.Stride), dst, 0, rowBytes);
                for (int x = 0; x < rowBytes; x += 4)
                {
                    // Average ClearType channels into neutral grayscale.
                    // Preserve full-intensity stems for a defined 12-13px font.
                    int coverage = (src[x] + src[x + 1] + src[x + 2] + 1) / 3;
                    int alpha = (coverage * color.A + 127) / 255;
                    if (alpha == 0) continue;
                    int inverse = 255 - alpha;
                    dst[x] = (byte)Math.Min(255, (color.B * alpha + 127) / 255 + (dst[x] * inverse + 127) / 255);
                    dst[x + 1] = (byte)Math.Min(255, (color.G * alpha + 127) / 255 + (dst[x + 1] * inverse + 127) / 255);
                    dst[x + 2] = (byte)Math.Min(255, (color.R * alpha + 127) / 255 + (dst[x + 2] * inverse + 127) / 255);
                    dst[x + 3] = (byte)Math.Min(255, alpha + (dst[x + 3] * inverse + 127) / 255);
                }
                Marshal.Copy(dst, 0, IntPtr.Add(destination.Scan0, y * destination.Stride), rowBytes);
            }
        }
        finally
        {
            if (destination is not null) target.UnlockBits(destination);
            mask.UnlockBits(source);
        }
    }
}
