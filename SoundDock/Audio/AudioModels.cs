namespace SoundDock.Audio;

/// <summary>An active playback endpoint reported by Windows Core Audio.</summary>
public sealed record AudioDevice(
    string Id,
    string Name,
    bool IsDefault,
    bool IsCommunicationsDefault);

/// <summary>
/// One shared-mode audio session on the current playback endpoint. A process
/// can own several sessions; Id is the session-instance ID, not the process ID.
/// </summary>
public sealed record AudioSession(
    string Id,
    string Name,
    uint ProcessId,
    float Volume,
    bool Muted,
    bool IsActive);

/// <summary>A COM-free snapshot that is safe for the UI to retain.</summary>
public sealed record AudioSnapshot(
    IReadOnlyList<AudioDevice> Devices,
    string? DefaultDeviceId,
    string? DefaultDeviceName,
    float MasterVolume,
    bool MasterMuted,
    IReadOnlyList<AudioSession> Sessions,
    string? Error)
{
    public bool HasOutput => DefaultDeviceId is not null;

    public static AudioSnapshot Empty(string? error = null) => new(
        Array.Empty<AudioDevice>(), null, null, 0f, false,
        Array.Empty<AudioSession>(), error);
}

/// <summary>An audio operation that the UI can report without terminating.</summary>
public sealed class AudioOperationException : Exception
{
    public AudioOperationException(string message) : base(message) { }
    public AudioOperationException(string message, Exception innerException)
        : base(message, innerException) { }
}
