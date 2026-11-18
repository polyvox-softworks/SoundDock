namespace SoundDock.Infrastructure;

internal static class AppDiagnostics
{
    internal static string LogPath => Path.Combine(SettingsStore.DataDirectory, "errors.log");

    internal static void Write(Exception? exception)
    {
        if (exception is null) return;
        try
        {
            Directory.CreateDirectory(SettingsStore.DataDirectory);
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 256 * 1024)
                File.Move(LogPath, LogPath + ".old", overwrite: true);
            File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:O}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A missing log directory must never prevent audio controls from working.
        }
    }
}
