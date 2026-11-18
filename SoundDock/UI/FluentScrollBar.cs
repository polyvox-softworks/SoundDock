using System.Drawing.Drawing2D;

namespace SoundDock.UI;

/// <summary>A compact, rounded scrollbar for the sound panel.</summary>
internal sealed class FluentScrollBar : Control
{
    private int _contentHeight;
    private int _viewportHeight;
    private int _offset;
    private bool _hovered;
    private bool _dragging;
    private int _dragStartY;
    private int _dragStartOffset;

    internal event Action<int>? ScrollRequested;

    public FluentScrollBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Background;
        TabStop = false;
        AccessibleRole = AccessibleRole.ScrollBar;
        AccessibleName = "Sound panel scrollbar";
        Width = 12;
    }

    private int S(int logicalPixels) =>
        (int)Math.Round(logicalPixels * DeviceDpi / 96f);

    private int Maximum => Math.Max(0, _contentHeight - _viewportHeight);

    internal void UpdateRange(int contentHeight, int viewportHeight, int offset)
    {
        int content = Math.Max(0, contentHeight);
        int viewport = Math.Max(0, viewportHeight);
        bool rangeChanged = _contentHeight != content || _viewportHeight != viewport;
        _contentHeight = content;
        _viewportHeight = viewport;
        SetScrollPosition(offset);
        if (rangeChanged) Invalidate();
    }

    internal void SetScrollPosition(int offset)
    {
        int next = Math.Clamp(offset, 0, Maximum);
        if (_offset == next) return;
        _offset = next;
        Invalidate();
    }

    private Rectangle ThumbBounds()
    {
        int inset = S(4);
        int trackHeight = Math.Max(0, Height - 2 * inset);
        if (trackHeight == 0 || _contentHeight <= 0) return Rectangle.Empty;

        int minimum = Math.Min(trackHeight, S(30));
        int thumbHeight = Math.Clamp(
            (int)Math.Round(trackHeight * (double)_viewportHeight / _contentHeight),
            minimum, trackHeight);
        int travel = trackHeight - thumbHeight;
        int y = inset + (Maximum == 0 ? 0 :
            (int)Math.Round(travel * (double)_offset / Maximum));
        int width = Math.Min(Width, S(_hovered || _dragging ? 7 : 4));
        return new Rectangle((Width - width) / 2, y, width, thumbHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Background);
        if (Maximum == 0) return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle thumb = ThumbBounds();
        if (thumb.Width <= 0 || thumb.Height <= 0) return;

        if (_hovered || _dragging)
        {
            int railWidth = Math.Max(S(1), 1);
            using var rail = new SolidBrush(SystemInformation.HighContrast
                ? SystemColors.Window : Color.FromArgb(53, 53, 53));
            e.Graphics.FillRectangle(rail, (Width - railWidth) / 2,
                S(4), railWidth, Math.Max(0, Height - S(8)));
        }

        Color color = SystemInformation.HighContrast ? SystemColors.Highlight :
            _dragging ? Color.FromArgb(196, 196, 196) :
            _hovered ? Color.FromArgb(158, 158, 158) : Color.FromArgb(112, 112, 112);
        using var brush = new SolidBrush(color);
        using var path = Theme.RoundedRectangle(
            new RectangleF(thumb.X, thumb.Y, thumb.Width, thumb.Height), thumb.Width / 2f);
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (!_dragging) _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && Maximum > 0)
        {
            Rectangle thumb = ThumbBounds();
            if (e.Y >= thumb.Top && e.Y <= thumb.Bottom)
            {
                _dragging = true;
                _dragStartY = e.Y;
                _dragStartOffset = _offset;
                Capture = true;
                Invalidate();
            }
            else
            {
                // Clicking the track moves one page, as in Windows Explorer.
                int direction = e.Y < thumb.Top ? -1 : 1;
                ScrollRequested?.Invoke(_offset + direction * Math.Max(1, _viewportHeight - S(24)));
            }
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging && Maximum > 0)
        {
            int trackHeight = Math.Max(0, Height - 2 * S(4));
            int travel = trackHeight - ThumbBounds().Height;
            if (travel > 0)
            {
                int next = _dragStartOffset +
                    (int)Math.Round((e.Y - _dragStartY) * (double)Maximum / travel);
                ScrollRequested?.Invoke(next);
            }
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && _dragging)
        {
            _dragging = false;
            Capture = false;
            _hovered = ClientRectangle.Contains(PointToClient(Cursor.Position));
            Invalidate();
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        if (!Capture && _dragging)
        {
            _dragging = false;
            Invalidate();
        }
        base.OnMouseCaptureChanged(e);
    }
}
