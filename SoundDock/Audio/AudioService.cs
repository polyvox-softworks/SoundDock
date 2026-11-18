using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SoundDock.Audio;

/// <summary>
/// Small, dependency-free Core Audio adapter. Create and call it on the same
/// COM-initialized UI thread. Every call reacquires and releases native objects
/// so device unplugging, sleep/resume and Audio Service restarts can recover on
/// the next refresh. No native object escapes into a snapshot or UI control.
/// </summary>
public sealed class AudioService : IDisposable
{
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private bool _disposed;

    public AudioService()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Core Audio is required.");
    }

    public AudioSnapshot CaptureSnapshot(bool includeSessions = true)
    {
        EnsureUsable();
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? currentDevice = null;
        try
        {
            enumerator = CreateEnumerator();
            currentDevice = GetDefaultDevice(enumerator, AudioRole.Multimedia);
            string? defaultId = currentDevice is null ? null : GetDeviceId(currentDevice);
            string? communicationsId = GetDefaultDeviceId(enumerator, AudioRole.Communications);
            var devices = GetDevices(enumerator, defaultId, communicationsId);

            if (currentDevice is null)
                return new AudioSnapshot(devices, null, null, 0f, false, Array.Empty<AudioSession>(), null);

            string defaultName = devices.FirstOrDefault(device => device.Id == defaultId)?.Name
                ?? GetDeviceName(currentDevice);
            float volume = 0f;
            bool muted = false;
            string? error = null;
            IAudioEndpointVolume? endpointVolume = null;
            try
            {
                endpointVolume = CoreAudioNative.Activate<IAudioEndpointVolume>(currentDevice);
                CoreAudioNative.Check(endpointVolume.GetMasterVolumeLevelScalar(out volume));
                CoreAudioNative.Check(endpointVolume.GetMute(out muted));
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                error = Explain(exception, "The output is changing or temporarily unavailable.");
            }
            finally { CoreAudioNative.Release(endpointVolume); }

            IReadOnlyList<AudioSession> sessions = Array.Empty<AudioSession>();
            if (includeSessions)
            {
                try { sessions = GetSessions(currentDevice); }
                catch (Exception exception) when (IsRecoverable(exception))
                {
                    error ??= Explain(exception, "App volume controls are temporarily unavailable.");
                }
            }

            return new AudioSnapshot(devices, defaultId, defaultName,
                Math.Clamp(volume, 0f, 1f), muted, sessions, error);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return AudioSnapshot.Empty(Explain(exception, "Windows audio is temporarily unavailable. It will be checked again automatically."));
        }
        finally
        {
            CoreAudioNative.Release(currentDevice);
            CoreAudioNative.Release(enumerator);
        }
    }

    public void SetMasterVolume(float volume)
    {
        ValidateVolume(volume);
        RunOperation("Could not change the output volume.", () => WithDefaultVolume(endpoint =>
        {
            var context = CoreAudioNative.EventContext;
            CoreAudioNative.Check(endpoint.SetMasterVolumeLevelScalar(volume, ref context));
        }));
    }

    public void SetMasterMute(bool muted) =>
        RunOperation("Could not change the output mute setting.", () => WithDefaultVolume(endpoint =>
        {
            var context = CoreAudioNative.EventContext;
            CoreAudioNative.Check(endpoint.SetMute(muted, ref context));
        }));

    public void SetSessionVolume(string sessionId, float volume)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ValidateVolume(volume);
        RunOperation("Could not change this app's volume.", () => WithSessionVolume(sessionId, session =>
        {
            var context = CoreAudioNative.EventContext;
            CoreAudioNative.Check(session.SetMasterVolume(volume, ref context));
        }));
    }

    public void SetSessionMute(string sessionId, bool muted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        RunOperation("Could not change this app's mute setting.", () => WithSessionVolume(sessionId, session =>
        {
            var context = CoreAudioNative.EventContext;
            CoreAudioNative.Check(session.SetMute(muted, ref context));
        }));
    }

    /// <summary>
    /// Sets the default playback device for multimedia and console roles, and
    /// optionally the communications role. Does not modify microphone devices
    /// or overwrite any application's explicitly chosen output device.
    /// </summary>
    public void SetDefaultOutput(string deviceId, bool includeCommunications = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        RunOperation("Could not switch the sound output.", () =>
        {
            IMMDeviceEnumerator? enumerator = null;
            IMMDevice? device = null;
            try
            {
                enumerator = CreateEnumerator();
                CoreAudioNative.Check(enumerator.GetDevice(deviceId, out device));
                CoreAudioNative.Check(device.GetState(out var state));
                CoreAudioNative.Check(((IMMEndpoint)device).GetDataFlow(out var flow));
                if (state != AudioDeviceState.Active || flow != AudioDataFlow.Render)
                    throw new AudioOperationException("This output is no longer connected. Select another output.");
                PolicyConfig.SetDefaultOutput(deviceId, includeCommunications);
            }
            finally
            {
                CoreAudioNative.Release(device);
                CoreAudioNative.Release(enumerator);
            }
        });
    }

    private static IReadOnlyList<AudioDevice> GetDevices(IMMDeviceEnumerator enumerator,
        string? defaultId, string? communicationsId)
    {
        IMMDeviceCollection? collection = null;
        var devices = new List<AudioDevice>();
        try
        {
            CoreAudioNative.Check(enumerator.EnumAudioEndpoints(AudioDataFlow.Render, AudioDeviceState.Active, out collection));
            CoreAudioNative.Check(collection.GetCount(out var count));
            for (uint index = 0; index < count; index++)
            {
                IMMDevice? device = null;
                try
                {
                    CoreAudioNative.Check(collection.Item(index, out device));
                    string id = GetDeviceId(device);
                    devices.Add(new AudioDevice(id, GetDeviceName(device), id == defaultId, id == communicationsId));
                }
                catch (Exception exception) when (IsRecoverable(exception))
                {
                    // A single endpoint can disappear while the collection is
                    // being read. Other connected outputs remain usable.
                }
                finally { CoreAudioNative.Release(device); }
            }
        }
        finally { CoreAudioNative.Release(collection); }
        return devices.OrderByDescending(device => device.IsDefault)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(device => device.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static string GetDeviceName(IMMDevice device)
    {
        IPropertyStore? properties = null;
        PropertyVariant value = default;
        try
        {
            CoreAudioNative.Check(device.OpenPropertyStore(0, out properties)); // STGM_READ
            var key = new PropertyKey(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
            CoreAudioNative.Check(properties.GetValue(ref key, out value)); // PKEY_Device_FriendlyName
            if (value.Type == 31 && value.Pointer != IntPtr.Zero) // VT_LPWSTR
            {
                string? name = Marshal.PtrToStringUni(value.Pointer);
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
        }
        catch (Exception exception) when (IsRecoverable(exception)) { }
        finally
        {
            CoreAudioNative.PropVariantClear(ref value);
            CoreAudioNative.Release(properties);
        }
        return "Audio output";
    }

    private static IReadOnlyList<AudioSession> GetSessions(IMMDevice device)
    {
        var result = new List<AudioSession>();
        WithSessionEnumerator(device, sessions =>
        {
            CoreAudioNative.Check(sessions.GetCount(out int count));
            for (int index = 0; index < count; index++)
            {
                IAudioSessionControl? control = null;
                try
                {
                    CoreAudioNative.Check(sessions.GetSession(index, out control));
                    var control2 = (IAudioSessionControl2)control;
                    CoreAudioNative.Check(control2.GetState(out var state));
                    if (state == AudioSessionState.Expired) continue;
                    CoreAudioNative.Check(control2.GetSessionInstanceIdentifier(out string id));
                    if (string.IsNullOrEmpty(id)) continue;
                    CoreAudioNative.Check(control2.GetProcessId(out uint processId));
                    var volume = (ISimpleAudioVolume)control;
                    CoreAudioNative.Check(volume.GetMasterVolume(out float level));
                    CoreAudioNative.Check(volume.GetMute(out bool muted));
                    string name = GetSessionName(control2, processId);
                    result.Add(new AudioSession(id, name, processId, Math.Clamp(level, 0f, 1f), muted,
                        state == AudioSessionState.Active));
                }
                catch (Exception exception) when (IsRecoverable(exception))
                {
                    // A process/session can close at any point in enumeration.
                }
                finally
                {
                    // control2 and volume alias this RCW; release only once.
                    CoreAudioNative.Release(control);
                }
            }
        });
        return result.OrderBy(session => session.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(session => session.Id, StringComparer.Ordinal).ToArray();
    }

    private static string GetSessionName(IAudioSessionControl2 control, uint processId)
    {
        if (control.IsSystemSoundsSession() == 0) return "System sounds";
        if (control.GetDisplayName(out var displayName) >= 0 && !string.IsNullOrWhiteSpace(displayName))
        {
            if (!displayName.StartsWith('@')) return displayName;
            var resolved = new StringBuilder(1024);
            if (CoreAudioNative.SHLoadIndirectString(displayName, resolved, (uint)resolved.Capacity, IntPtr.Zero) >= 0
                && resolved.Length > 0) return resolved.ToString();
        }

        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            string name = process.ProcessName;
            // File descriptions are friendlier than process names. Reading an
            // elevated process may be denied; the accessible name still works.
            try
            {
                string? description = process.MainModule?.FileVersionInfo.FileDescription;
                if (!string.IsNullOrWhiteSpace(description)) return description;
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException
                or NotSupportedException or IOException or UnauthorizedAccessException) { }
            return string.IsNullOrWhiteSpace(name) ? $"App {processId}" : name;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or Win32Exception or OverflowException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return processId == 0 ? "Audio session" : $"App {processId}";
        }
    }

    private static void WithDefaultVolume(Action<IAudioEndpointVolume> action)
    {
        WithDefaultDevice(device =>
        {
            IAudioEndpointVolume? volume = null;
            try
            {
                volume = CoreAudioNative.Activate<IAudioEndpointVolume>(device);
                action(volume);
            }
            finally { CoreAudioNative.Release(volume); }
        });
    }

    private static void WithSessionVolume(string id, Action<ISimpleAudioVolume> action)
    {
        bool found = false;
        WithDefaultDevice(device => WithSessionEnumerator(device, sessions =>
        {
            CoreAudioNative.Check(sessions.GetCount(out int count));
            for (int index = 0; index < count; index++)
            {
                IAudioSessionControl? control = null;
                try
                {
                    if (sessions.GetSession(index, out control) < 0 || control is null) continue;
                    var control2 = (IAudioSessionControl2)control;
                    if (control2.GetSessionInstanceIdentifier(out string currentId) < 0 || currentId != id) continue;
                    action((ISimpleAudioVolume)control);
                    found = true;
                    break;
                }
                finally { CoreAudioNative.Release(control); }
            }
        }));
        if (!found)
            throw new AudioOperationException("This app's audio session has ended or moved to another output. The mixer will refresh automatically.");
    }

    private static void WithSessionEnumerator(IMMDevice device, Action<IAudioSessionEnumerator> action)
    {
        IAudioSessionManager2? manager = null;
        IAudioSessionEnumerator? sessions = null;
        try
        {
            manager = CoreAudioNative.Activate<IAudioSessionManager2>(device);
            CoreAudioNative.Check(manager.GetSessionEnumerator(out sessions));
            action(sessions);
        }
        finally
        {
            CoreAudioNative.Release(sessions);
            CoreAudioNative.Release(manager);
        }
    }

    private static void WithDefaultDevice(Action<IMMDevice> action)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            enumerator = CreateEnumerator();
            device = GetDefaultDevice(enumerator, AudioRole.Multimedia);
            if (device is null) throw new AudioOperationException("No sound output is connected. Connect headphones or speakers and try again.");
            action(device);
        }
        finally
        {
            CoreAudioNative.Release(device);
            CoreAudioNative.Release(enumerator);
        }
    }

    private static IMMDeviceEnumerator CreateEnumerator() => (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

    private static IMMDevice? GetDefaultDevice(IMMDeviceEnumerator enumerator, AudioRole role)
    {
        int result = enumerator.GetDefaultAudioEndpoint(AudioDataFlow.Render, role, out var device);
        if (result == CoreAudioNative.ErrorNotFound)
        {
            CoreAudioNative.Release(device);
            return null;
        }
        if (result < 0)
        {
            CoreAudioNative.Release(device);
            CoreAudioNative.Check(result);
        }
        return device;
    }

    private static string? GetDefaultDeviceId(IMMDeviceEnumerator enumerator, AudioRole role)
    {
        IMMDevice? device = null;
        try
        {
            device = GetDefaultDevice(enumerator, role);
            return device is null ? null : GetDeviceId(device);
        }
        finally { CoreAudioNative.Release(device); }
    }

    private static string GetDeviceId(IMMDevice device)
    {
        CoreAudioNative.Check(device.GetId(out string id));
        return id;
    }

    private void RunOperation(string message, Action action)
    {
        EnsureUsable();
        try { action(); }
        catch (AudioOperationException) { throw; }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            throw new AudioOperationException(Explain(exception, message), exception);
        }
    }

    private static void ValidateVolume(float volume)
    {
        if (!float.IsFinite(volume) || volume is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be between 0 and 1.");
    }

    private static bool IsRecoverable(Exception exception) =>
        exception is COMException or InvalidCastException or UnauthorizedAccessException or Win32Exception;

    private static string Explain(Exception exception, string fallback) =>
        $"{fallback} (0x{exception.HResult:X8})";

    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _threadId)
            throw new InvalidOperationException("Use AudioService only on the UI thread that created it.");
    }

    public void Dispose()
    {
        // Native references are already released at the end of every method.
        _disposed = true;
    }
}
