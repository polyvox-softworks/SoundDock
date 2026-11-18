using System.Drawing;

namespace SoundDock.UI;

internal sealed partial class MixerFlyout
{
    private int _scrollRemainder;

    // The wheel navigates the flyout. Volume only changes through the sliders,
    // their keyboard shortcuts, or the taskbar indicator.
    public bool PreFilterMessage(ref Message message)
    {
        const int wmMouseWheel = 0x020A;
        const int wheelDelta = 120;

        if (message.Msg != wmMouseWheel || !Visible || _opening)
            return false;

        long packedPoint = message.LParam.ToInt64();
        var screenPoint = new Point(
            unchecked((short)(packedPoint & 0xffff)),
            unchecked((short)((packedPoint >> 16) & 0xffff)));

        if (!Bounds.Contains(screenPoint))
            return false;

        // Use one scroll target, even when a slider, mute button, or device row
        // has keyboard focus. This avoids accidental volume changes while browsing.
        int maxScroll = Math.Max(0, _naturalHeight - _body.ClientSize.Height);
        if (maxScroll == 0)
            return false;

        int lines = SystemInformation.MouseWheelScrollLines;
        if (lines == 0)
            return true;

        int delta = unchecked((short)((message.WParam.ToInt64() >> 16) & 0xffff));
        _scrollRemainder += delta;
        int steps = _scrollRemainder / wheelDelta;
        _scrollRemainder %= wheelDelta;
        if (steps == 0)
            return true;

        int distance = lines < 0
            ? Math.Max(S(60), _body.ClientSize.Height - S(40))
            : Math.Max(S(12), lines * S(18));

        int current = _scrollOffset;
        int next = (int)Math.Clamp((long)current - (long)steps * distance, 0L, maxScroll);
        if (next != current)
            ScrollTo(next);

        return true;
    }
}
