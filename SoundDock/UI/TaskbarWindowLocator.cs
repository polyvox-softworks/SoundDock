namespace SoundDock.UI;

/// <summary>Resolves Explorer taskbars by the display they occupy.</summary>
internal static class TaskbarWindowLocator
{
    internal static IntPtr FindForDisplay(string displayName)
    {
        IntPtr primary = WidgetNative.FindWindow("Shell_TrayWnd", null);
        if (MatchesDisplay(primary, displayName)) return primary;

        IntPtr previous = IntPtr.Zero;
        for (int i = 0; i < 32; i++)
        {
            IntPtr secondary = WidgetNative.FindWindowEx(IntPtr.Zero, previous,
                "Shell_SecondaryTrayWnd", null);
            if (secondary == IntPtr.Zero) break;
            if (MatchesDisplay(secondary, displayName)) return secondary;
            previous = secondary;
        }
        return IntPtr.Zero;
    }

    private static bool MatchesDisplay(IntPtr taskbar, string displayName)
    {
        if (taskbar == IntPtr.Zero || !WidgetNative.IsWindow(taskbar) ||
            !WidgetNative.IsWindowVisible(taskbar) ||
            !WidgetNative.GetWindowRect(taskbar, out var native)) return false;
        Rectangle bounds = native.ToRectangle();
        if (bounds.Width <= 0 || bounds.Height <= 0) return false;
        var display = Screen.FromRectangle(bounds);
        return string.Equals(display.DeviceName, displayName, StringComparison.OrdinalIgnoreCase);
    }
}
