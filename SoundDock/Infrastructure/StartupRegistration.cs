using Microsoft.Win32;

namespace SoundDock.Infrastructure;

internal static class StartupRegistration
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    internal static bool CanRegister => Environment.ProcessPath is { } path &&
        path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Path.GetFileNameWithoutExtension(path), "dotnet", StringComparison.OrdinalIgnoreCase);

    internal static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return HasEntry(key, AppIdentity.Name) ||
                   PreviousVersions.InPriorityOrder.Any(app => HasEntry(key, app.StartupValue));
        }
    }

    // Keep a current registration pointed at this executable. An intentionally
    // removed registration is not recreated after initial setup.
    internal static void RefreshExistingEntry()
    {
        if (!CanRegister) return;
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        if (key is null) return;

        bool hasRegistration = HasEntry(key, AppIdentity.Name) ||
            PreviousVersions.InPriorityOrder.Any(app => HasEntry(key, app.StartupValue));

        if (hasRegistration)
            key.SetValue(AppIdentity.Name, StartupCommand(), RegistryValueKind.String);

        RemovePreviousEntries(key);
    }

    internal static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true)
                        ?? throw new IOException("Windows did not allow access to startup settings.");
        if (enabled)
        {
            if (!CanRegister)
                throw new InvalidOperationException("Build SoundDock before enabling startup.");
            key.SetValue(AppIdentity.Name, StartupCommand(), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(AppIdentity.Name, throwOnMissingValue: false);
        }
        RemovePreviousEntries(key);
    }

    private static void RemovePreviousEntries(RegistryKey key)
    {
        foreach (var previous in PreviousVersions.InPriorityOrder)
            key.DeleteValue(previous.StartupValue, throwOnMissingValue: false);
    }

    private static bool HasEntry(RegistryKey? key, string name) =>
        key?.GetValue(name) is string value && !string.IsNullOrWhiteSpace(value);

    private static string StartupCommand() => $"\"{Environment.ProcessPath}\" --background";
}
