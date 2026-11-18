using System.Runtime.InteropServices;
using SoundDock.Infrastructure;
using SoundDock.UI;

namespace SoundDock;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var instance = new Mutex(true, AppIdentity.MutexName, out bool firstInstance);
        if (!firstInstance)
        {
            NativeMethods.PostMessage(new IntPtr(0xFFFF), NativeMethods.ShowMixerMessage,
                IntPtr.Zero, IntPtr.Zero);
            return;
        }

        try
        {
            ApplicationConfiguration.Initialize();
            if (RunningPreviousVersion() is { } previousName)
            {
                MessageBox.Show($"Close {previousName} before starting SoundDock. " +
                                "Right-click its taskbar indicator and choose Exit.",
                    AppIdentity.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Theme.RefreshAccent();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => ShowFatalError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                AppDiagnostics.Write(e.ExceptionObject as Exception);

            using var context = new AudioAppContext(
                args.Contains("--background", StringComparer.OrdinalIgnoreCase));
            Application.Run(context);
        }
        catch (Exception ex)
        {
            ShowFatalError(ex);
        }
        finally
        {
            instance.ReleaseMutex();
        }
    }

    private static string? RunningPreviousVersion()
    {
        foreach (var oldApp in PreviousVersions.InPriorityOrder)
        {
            if (!Mutex.TryOpenExisting(oldApp.MutexName, out var previous))
                continue;

            using (previous)
            {
                try
                {
                    if (!previous.WaitOne(0)) return oldApp.DisplayName;
                }
                catch (AbandonedMutexException)
                {
                    // WaitOne acquired the abandoned mutex.
                }

                previous.ReleaseMutex();
            }
        }
        return null;
    }

    private static void ShowFatalError(Exception ex)
    {
        AppDiagnostics.Write(ex);
        MessageBox.Show($"SoundDock could not continue.\n\n{ex.Message}\n\nLog: {AppDiagnostics.LogPath}",
            AppIdentity.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Exit();
    }
}

internal static class NativeMethods
{
    internal static readonly uint ShowMixerMessage = RegisterWindowMessage("SoundDock.ShowMixer.8F059D85");
    internal static readonly uint VolumeChangedMessage = RegisterWindowMessage("SoundDock.VolumeChanged.8F059D85");

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetColorizationColor(out uint colorizationColor,
        [MarshalAs(UnmanagedType.Bool)] out bool opaqueBlend);
}
