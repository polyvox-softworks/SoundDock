using System.Runtime.InteropServices;

namespace SoundDock.Audio;

// Native signatures follow the Windows SDK's mmdeviceapi.h, endpointvolume.h,
// audiopolicy.h and audioclient.h. Keep their native vtable order intact.
internal enum AudioDataFlow { Render, Capture, All }
internal enum AudioRole { Console, Multimedia, Communications }
[Flags]
internal enum AudioDeviceState : uint { Active = 1, Disabled = 2, NotPresent = 4, Unplugged = 8 }
internal enum AudioSessionState { Inactive, Active, Expired }

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(AudioDataFlow flow, AudioDeviceState stateMask, out IMMDeviceCollection devices);
    [PreserveSig] int GetDefaultAudioEndpoint(AudioDataFlow flow, AudioRole role, out IMMDevice? device);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int Item(uint index, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr activationParameters,
        [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out AudioDeviceState state);
}

[ComImport, Guid("1BE09788-6894-4089-8586-9A2A6C265AC5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMEndpoint
{
    [PreserveSig] int GetDataFlow(out AudioDataFlow flow);
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct PropertyKey
{
    public Guid FormatId;
    public uint PropertyId;
    public PropertyKey(Guid formatId, uint propertyId) { FormatId = formatId; PropertyId = propertyId; }
}

// Two pointer-sized union fields yield the native PROPVARIANT sizes on both
// 64-bit (24 bytes) and 32-bit (16 bytes) Windows. We read only VT_LPWSTR.
[StructLayout(LayoutKind.Sequential)]
internal struct PropertyVariant
{
    public ushort Type;
    private ushort _reserved1;
    private ushort _reserved2;
    private ushort _reserved3;
    public IntPtr Pointer;
    private IntPtr _unionRemainder;
}

[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, out PropertyKey key);
    [PreserveSig] int GetValue(ref PropertyKey key, out PropertyVariant value);
    [PreserveSig] int SetValue(ref PropertyKey key, ref PropertyVariant value);
    [PreserveSig] int Commit();
}

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
    [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
    [PreserveSig] int GetChannelCount(out uint count);
    [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
    [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
    [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
    [PreserveSig] int VolumeStepUp(ref Guid context);
    [PreserveSig] int VolumeStepDown(ref Guid context);
    [PreserveSig] int QueryHardwareSupport(out uint supportMask);
    [PreserveSig] int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
}

// Implemented by this app (not [ComImport]): Core Audio calls OnNotify from
// an arbitrary COM thread. Never enter WinForms or query audio devices there.
[ComVisible(true), Guid("657804FA-D6AD-4496-8A60-352752AF4F89"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolumeCallback
{
    [PreserveSig] int OnNotify(IntPtr notificationData);
}

[StructLayout(LayoutKind.Sequential)]
internal struct AudioVolumeNotificationData
{
    public Guid EventContext;
    [MarshalAs(UnmanagedType.Bool)] public bool Muted;
    public float MasterVolume;
    public uint ChannelCount;
    // Native structure continues with a variable-length channel array.
}

[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    // IAudioSessionManager base-interface slots.
    [PreserveSig] int GetAudioSessionControl(ref Guid session, uint flags, out IAudioSessionControl control);
    [PreserveSig] int GetSimpleAudioVolume(ref Guid session, uint flags, out ISimpleAudioVolume volume);
    [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    [PreserveSig] int RegisterSessionNotification(IntPtr notification);
    [PreserveSig] int UnregisterSessionNotification(IntPtr notification);
    [PreserveSig] int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr notification);
    [PreserveSig] int UnregisterDuckNotification(IntPtr notification);
}

[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int GetSession(int index, out IAudioSessionControl session);
}

[ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl
{
    [PreserveSig] int GetState(out AudioSessionState state);
    [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
    [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
    [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
    [PreserveSig] int GetGroupingParam(out Guid grouping);
    [PreserveSig] int SetGroupingParam(ref Guid grouping, ref Guid context);
    [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
    [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
}

[ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    // Repeat the IAudioSessionControl slots to preserve native inheritance.
    [PreserveSig] int GetState(out AudioSessionState state);
    [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
    [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
    [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
    [PreserveSig] int GetGroupingParam(out Guid grouping);
    [PreserveSig] int SetGroupingParam(ref Guid grouping, ref Guid context);
    [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
    [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
    [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetProcessId(out uint processId);
    [PreserveSig] int IsSystemSoundsSession();
    [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}

[ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    [PreserveSig] int SetMasterVolume(float level, ref Guid context);
    [PreserveSig] int GetMasterVolume(out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
}

internal static class CoreAudioNative
{
    internal const int ErrorNotFound = unchecked((int)0x80070490);
    internal const uint ClassContextAll = 23;
    internal static readonly Guid EventContext = new("AF3CE346-13F6-4D42-BC03-57356CF8176A");

    internal static void Check(int result)
    {
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }

    internal static T Activate<T>(IMMDevice device) where T : class
    {
        object? instance = null;
        try
        {
            var iid = typeof(T).GUID;
            Check(device.Activate(ref iid, ClassContextAll, IntPtr.Zero, out instance));
            return (T)instance;
        }
        catch
        {
            Release(instance);
            throw;
        }
    }

    // Release exactly the RCW reference acquired by a COM return value. Casts
    // to another interface are aliases and must NOT be released separately.
    internal static void Release(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
            Marshal.ReleaseComObject(instance);
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    internal static extern int PropVariantClear(ref PropertyVariant variant);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int SHLoadIndirectString(string source,
        System.Text.StringBuilder output, uint outputCharacters, IntPtr reserved);
}
