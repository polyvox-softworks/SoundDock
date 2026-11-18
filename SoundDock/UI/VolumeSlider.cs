using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace SoundDock.UI;

/// <summary>A compact, keyboard accessible volume slider with a value from 0 to 100.</summary>
public sealed class VolumeSlider : Control
{
    private int _value;
    private bool _hovered;

    public VolumeSlider()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
        BackColor = Color.Transparent;
        TabStop = true;
        AccessibleRole = AccessibleRole.Slider;
        AccessibleName = "Volume";
        Size = new Size(220, 34);
        MinimumSize = new Size(48, 24);
    }

    [Category("Behavior")]
    [DefaultValue(0)]
    public int Value
    {
        get => _value;
        set => SetValue(value, raiseEvent: true);
    }

    [Browsable(false)]
    public bool IsInteracting => Capture;

    [Category("Behavior")]
    public event EventHandler? ValueChanged;

    /// <summary>Synchronizes an external volume change without echoing it back to the audio API.</summary>
    public void SetValueSilently(int value)
    {
        // An external refresh must not fight a user who is dragging the thumb.
        if (!IsInteracting)
            SetValue(value, raiseEvent: false);
    }

    private float DpiScale => DeviceDpi / 96f;
    private float TrackStart => Math.Min(10f * DpiScale, Width / 2f);
    private float TrackEnd => Math.Max(TrackStart, Width - TrackStart);

    private void SetValue(int value, bool raiseEvent)
    {
        int next = Math.Clamp(value, 0, 100);
        if (_value == next)
            return;

        _value = next;
        Invalidate();
        if (IsHandleCreated)
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        if (raiseEvent)
            ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetFromPosition(int x)
    {
        float length = TrackEnd - TrackStart;
        if (length > 0f)
            Value = (int)Math.Round(100f * Math.Clamp((x - TrackStart) / length, 0f, 1f));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        bool highContrast = SystemInformation.HighContrast;
        Color accent = highContrast ? SystemColors.Highlight : Theme.Accent;
        Color track = highContrast ? SystemColors.GrayText : Color.FromArgb(95, 95, 95);
        Color thumb = highContrast ? SystemColors.WindowText : Color.FromArgb(250, 250, 250);
        if (!Enabled)
        {
            accent = highContrast ? SystemColors.GrayText : Color.FromArgb(94, 94, 94);
            thumb = highContrast ? SystemColors.GrayText : Color.FromArgb(132, 132, 132);
        }

        float y = Height / 2f;
        float x = TrackStart + (TrackEnd - TrackStart) * _value / 100f;
        float thickness = Math.Max(2f, 3.5f * DpiScale);
        using var trackPen = new Pen(track, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var fillPen = new Pen(accent, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        if (TrackEnd > TrackStart)
        {
            graphics.DrawLine(trackPen, TrackStart, y, TrackEnd, y);
            if (_value > 0)
                graphics.DrawLine(fillPen, TrackStart, y, x, y);
        }

        float radius = (_hovered || Capture ? 5.8f : 5f) * DpiScale;
        using var thumbBrush = new SolidBrush(thumb);
        graphics.FillEllipse(thumbBrush, x - radius, y - radius, radius * 2f, radius * 2f);

        if (Focused && ShowFocusCues && Enabled)
        {
            float focusRadius = 8f * DpiScale;
            using var focusPen = new Pen(accent, Math.Max(1f, DpiScale));
            graphics.DrawEllipse(focusPen, x - focusRadius, y - focusRadius,
                                 focusRadius * 2f, focusRadius * 2f);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && Enabled)
        {
            Focus();
            Capture = true;
            SetFromPosition(e.X);
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (Capture && Enabled)
            SetFromPosition(e.X);
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && Capture)
        {
            if (Enabled)
                SetFromPosition(e.X);
            Capture = false;
            Invalidate();
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        Invalidate();
        base.OnMouseCaptureChanged(e);
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

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        if (!Enabled)
            Capture = false;
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down or
            Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Enabled && e.Modifiers == Keys.None)
        {
            int? next = e.KeyCode switch
            {
                Keys.Left or Keys.Down => _value - 2,
                Keys.Right or Keys.Up => _value + 2,
                Keys.PageDown => _value - 10,
                Keys.PageUp => _value + 10,
                Keys.Home => 0,
                Keys.End => 100,
                _ => null
            };
            if (next.HasValue)
            {
                Value = next.Value;
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape && Capture)
            {
                Capture = false;
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }
        base.OnKeyDown(e);
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new SliderAccessibleObject(this);

    private sealed class SliderAccessibleObject(VolumeSlider owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.Slider;
        public override string? DefaultAction => "Adjust volume";
        public override string? Description =>
            "Use arrow keys to adjust volume, Page Up or Page Down for ten percent, Home for zero, or End for maximum.";

        public override string? Value
        {
            get => owner.Value.ToString(CultureInfo.CurrentCulture) + "%";
            set
            {
                if (owner.Enabled && int.TryParse(value?.Trim().TrimEnd('%'),
                        NumberStyles.Integer, CultureInfo.CurrentCulture, out int parsed))
                    owner.Value = parsed;
            }
        }

        public override void DoDefaultAction()
        {
            if (owner.Enabled)
                owner.Focus();
        }
    }
}
