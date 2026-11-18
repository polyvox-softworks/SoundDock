using System.Runtime.InteropServices;

namespace SoundDock.Audio;

/// <summary>
/// The one undocumented dependency in the app, isolated so it can be replaced
/// if Microsoft changes its private policy interface. No registry hacks,
/// Explorer injection, endpoint-format changes, or process routing are used.
/// </summary>
internal static class PolicyConfig
{
    internal static void SetDefaultOutput(string deviceId, bool includeCommunications)
    {
        IPolicyConfig? policy = null;
        int changedRoles = 0;
        try
        {
            policy = (IPolicyConfig)new PolicyConfigClient();
            SetRole(AudioRole.Multimedia);
            SetRole(AudioRole.Console);
            if (includeCommunications) SetRole(AudioRole.Communications);

            void SetRole(AudioRole role)
            {
                CoreAudioNative.Check(policy.SetDefaultEndpoint(deviceId, role));
                changedRoles++;
            }
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException)
        {
            string detail = changedRoles == 0
                ? "Windows could not switch the default output. Try selecting it in Windows Sound settings."
                : "Windows switched some audio roles but could not switch all of them. Check the default output and communications output in Windows Sound settings.";
            throw new AudioOperationException($"{detail} (0x{exception.HResult:X8})", exception);
        }
        finally { CoreAudioNative.Release(policy); }
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class PolicyConfigClient { }

    // The ResetDeviceFormat entry is essential: SetDefaultEndpoint is native
    // slot 10 after IUnknown. Omitting the reset slot calls the wrong method.
    // Observed interface layout: https://github.com/frgnca/AudioDeviceCmdlets/blob/master/SOURCE/IPolicyConfig.cs
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int defaultFormat, out IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int defaultPeriod, out long period, out long minimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, ref long period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, int effectsStore, ref PropertyKey key, out PropertyVariant value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, int effectsStore, ref PropertyKey key, ref PropertyVariant value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, AudioRole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }
}
