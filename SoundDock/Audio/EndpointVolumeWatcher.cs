using System.Runtime.InteropServices;
using SoundDock.Infrastructure;

namespace SoundDock.Audio;

/// <summary>
/// Low-latency Core Audio endpoint notifications. The callback only posts
/// small value messages to the STA message window, never touches WinForms/COM.
/// Device changes re-register the watcher; the 1.5-second snapshot is fallback.
/// </summary>
internal sealed class EndpointVolumeWatcher : IDisposable
{
    private readonly IntPtr _window;
    private IAudioEndpointVolume? _endpoint;
    private NotificationCallback? _callback;
    private string? _watchedId;
    private bool _disposed;

    public int Generation { get; private set; }

    internal EndpointVolumeWatcher(IntPtr window) => _window = window;

    public void Watch(string? deviceId)
    {
        if (_disposed || string.Equals(_watchedId, deviceId, StringComparison.Ordinal)) return;
        Stop();
        Generation++;
        if (string.IsNullOrEmpty(deviceId)) return;

        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioEndpointVolume? endpoint = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            CoreAudioNative.Check(enumerator.GetDevice(deviceId, out device));
            endpoint = CoreAudioNative.Activate<IAudioEndpointVolume>(device);
            var callback = new NotificationCallback(_window, Generation);
            CoreAudioNative.Check(endpoint.RegisterControlChangeNotify(callback));
            _endpoint = endpoint;
            _callback = callback;
            _watchedId = deviceId;
            endpoint = null; // registered endpoint is now owned by this watcher
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException
            or NotSupportedException or PlatformNotSupportedException)
        {
            // Snapshot polling continues working even when notifications fail.
            AppDiagnostics.Write(ex);
        }
        finally
        {
            CoreAudioNative.Release(endpoint);
            CoreAudioNative.Release(device);
            CoreAudioNative.Release(enumerator);
        }
    }

    private void Stop()
    {
        if (_endpoint is not null)
        {
            if (_callback is not null)
            {
                try { CoreAudioNative.Check(_endpoint.UnregisterControlChangeNotify(_callback)); }
                catch (Exception ex) when (ex is COMException or InvalidOperationException)
                {
                    AppDiagnostics.Write(ex);
                }
            }
            CoreAudioNative.Release(_endpoint);
        }
        _endpoint = null;
        _callback = null;
        _watchedId = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        Generation++;
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class NotificationCallback(IntPtr window, int generation) : IAudioEndpointVolumeCallback
    {
        [PreserveSig]
        public int OnNotify(IntPtr notificationData)
        {
            // AUDIO_VOLUME_NOTIFICATION_DATA contains the endpoint's latest
            // scalar and mute state; convey them directly for instant redraw.
            try
            {
                if (notificationData == IntPtr.Zero) return 0;
                var data = Marshal.PtrToStructure<AudioVolumeNotificationData>(notificationData);
                if (!float.IsFinite(data.MasterVolume)) return 0;
                int scaled = (int)Math.Round(Math.Clamp(data.MasterVolume, 0f, 1f) * 10000f);
                int packed = scaled | (data.Muted ? 0x10000 : 0);
                NativeMethods.PostMessage(window, NativeMethods.VolumeChangedMessage,
                    new IntPtr(packed), new IntPtr(generation));
            }
            catch (Exception)
            {
                // Never throw across the Core Audio callback boundary.
            }
            return 0;
        }
    }
}
