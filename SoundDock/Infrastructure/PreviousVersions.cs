namespace SoundDock.Infrastructure;

internal readonly record struct PreviousVersion(
    string DisplayName,
    string DataFolder,
    string StartupValue,
    string MutexName);

internal static class PreviousVersions
{
    // Most recent version first, so newer preferences take precedence.
    internal static readonly PreviousVersion[] InPriorityOrder =
    [
        new("BarTone", "BarTone", "BarTone",
            @"Local\BarTone.8F059D85-3950-437B-BDF2-BDB65CC85E55"),
        new("Taskbar Audio", "TaskbarAudio", "TaskbarAudio",
            @"Local\TaskbarAudio.8F059D85-3950-437B-BDF2-BDB65CC85E55")
    ];
}
