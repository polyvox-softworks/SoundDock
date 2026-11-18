using System.Drawing;

namespace SoundDock.UI;

// Mouse-wheel routing is kept separate from the flyout layout.
internal sealed partial class MixerFlyout
{
    private MixerRow? _wheelRow;
    private int _wheelRemainder;

    // Route wheel input by pointer position, not keyboard focus.
    public bool PreFilterMessage(ref Message message)
    {
        const int wmMouseWheel = 0x020A;
        const int wheelDelta = 120;
        if (message.Msg != wmMouseWheel || !Visible || _opening ||
            _deviceMenu.Visible || _hiddenMenu.Visible || (!Focused && !ContainsFocus))
            return false;

        long packedPoint = message.LParam.ToInt64();
        var point = new Point(unchecked((short)(packedPoint & 0xffff)),
                              unchecked((short)((packedPoint >> 16) & 0xffff)));
        if (!Bounds.Contains(point)) return false;

        bool overBody = _body.RectangleToScreen(_body.ClientRectangle).Contains(point);
        bool inScrollbar = overBody && _body.VerticalScroll.Visible &&
            point.X >= _body.RectangleToScreen(_body.ClientRectangle).Right - SystemInformation.VerticalScrollBarWidth;
        if (inScrollbar) return false;

        MixerRow? target = null;
        if (overBody)
        {
            if (_master.Visible && _master.Enabled &&
                _master.RectangleToScreen(_master.ClientRectangle).Contains(point))
                target = _master;
            else
                target = _sessionRows.Values.FirstOrDefault(row => row.Visible && row.Enabled &&
                    row.RectangleToScreen(row.ClientRectangle).Contains(point));
        }

        // Preserve mouse-wheel navigation through long device/app lists.
        if (target is null && overBody && _body.VerticalScroll.Visible)
        {
            _wheelRow = null;
            _wheelRemainder = 0;
            return false;
        }
        target ??= _master;
        if (!target.Enabled) return false;
        if (_wheelRow != target)
        {
            _wheelRow = target;
            _wheelRemainder = 0;
        }

        int delta = unchecked((short)((message.WParam.ToInt64() >> 16) & 0xffff));
        _wheelRemainder += delta;
        int notches = _wheelRemainder / wheelDelta;
        _wheelRemainder %= wheelDelta;
        if (notches != 0)
            target.AdjustVolume(Math.Clamp(notches * 2, -100, 100));
        return true;
    }
}
