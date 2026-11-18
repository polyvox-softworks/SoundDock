using System.Text.Json;

namespace SoundDock.Infrastructure;

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string LocalAppData =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    internal static string DataDirectory => Path.Combine(LocalAppData, AppIdentity.Name);

    private static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    internal static AppSettings Load()
    {
        if (File.Exists(SettingsPath))
            return Read(SettingsPath) ?? new AppSettings();

        foreach (var previous in PreviousVersions.InPriorityOrder)
        {
            string path = Path.Combine(LocalAppData, previous.DataFolder, "settings.json");
            if (!File.Exists(path)) continue;

            var settings = Read(path);
            if (settings is null) continue;

            try { Save(settings); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppDiagnostics.Write(ex);
            }
            return settings;
        }
        return new AppSettings();
    }

    private static AppSettings? Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppDiagnostics.Write(ex);
            return null;
        }
    }

    internal static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        string temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, SettingsPath, overwrite: true);
    }
}
