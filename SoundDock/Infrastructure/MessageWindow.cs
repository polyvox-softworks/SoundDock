using System.Runtime.InteropServices;

namespace SoundDock;

/// <summary>
/// An invisible top-level window receives second-instance activation and
/// Explorer's taskbar-created broadcast. A message-only window cannot do this.
/// </summary>
internal sealed class MessageWindow : NativeWindow, IDisposable
{
    private const uint ErrorMessage = 0x8035; // WM_APP + an app-private message.
    private static readonly uint TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
    private readonly Action _show;
    private readonly Action _refreshPlacement;
    private readonly Action<string> _showError;
    private readonly Action<int, bool, int> _volumeChanged;
    private readonly Action _refreshAccent;
    private readonly Queue<string> _pendingErrors = new();

    internal MessageWindow(Action show, Action refreshPlacement, Action<string> showError,
        Action<int, bool, int> volumeChanged, Action refreshAccent)
    {
        _show = show;
        _refreshPlacement = refreshPlacement;
        _showError = showError;
        _volumeChanged = volumeChanged;
        _refreshAccent = refreshAccent;
        CreateHandle(new CreateParams
        {
            Caption = "SoundDock Message Window",
            Parent = IntPtr.Zero,
            Style = 0,
            ExStyle = 0x00000080 // WS_EX_TOOLWINDOW; no Alt+Tab/taskbar button.
        });
    }

    internal bool PostError(string message)
    {
        if (Handle == IntPtr.Zero) return false;
        _pendingErrors.Enqueue(message);
        if (NativeMethods.PostMessage(Handle, ErrorMessage, IntPtr.Zero, IntPtr.Zero)) return true;
        _pendingErrors.Clear();
        return false;
    }

    protected override void WndProc(ref Message message)
    {
        if ((uint)message.Msg == NativeMethods.ShowMixerMessage)
        {
            _show();
            message.Result = IntPtr.Zero;
            return;
        }
        if ((uint)message.Msg == TaskbarCreatedMessage)
        {
            _refreshPlacement();
            message.Result = IntPtr.Zero;
            return;
        }
        if ((uint)message.Msg == ErrorMessage)
        {
            if (_pendingErrors.TryDequeue(out string? text)) _showError(text);
            message.Result = IntPtr.Zero;
            return;
        }
        if ((uint)message.Msg == NativeMethods.VolumeChangedMessage)
        {
            int packed = unchecked((int)message.WParam.ToInt64());
            int generation = unchecked((int)message.LParam.ToInt64());
            _volumeChanged(packed & 0xFFFF, (packed & 0x10000) != 0, generation);
            message.Result = IntPtr.Zero;
            return;
        }
        // Refresh after monitor hotplug, display scaling and taskbar-setting changes.
        if (message.Msg is 0x007E or 0x001A or 0x02E0)
            _refreshPlacement();
        // Windows broadcasts these when Personalization -> Colors changes.
        if (message.Msg is 0x0320 or 0x001A or 0x031A)
            _refreshAccent();
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        _pendingErrors.Clear();
        DestroyHandle();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);
}
