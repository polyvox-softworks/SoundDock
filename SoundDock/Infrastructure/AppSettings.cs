namespace SoundDock.Infrastructure;

internal sealed class AppSettings
{
    private int _widgetOffset;

    public bool ShowPercentage { get; set; } = true;
    public bool SwitchCallOutput { get; set; } = true;
    public bool HasOpened { get; set; }
    public bool StartupInitialized { get; set; }

    // All = every enabled taskbar, Primary = main taskbar, Selected = one display.
    // Existing installations default to All without changing their other settings.
    public string TaskbarDisplayMode { get; set; } = "All";
    public string SelectedTaskbarDisplay { get; set; } = "";
    public Dictionary<string, int> DisplayOffsets { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    // Audio endpoint IDs remain stable across ordinary reboots. Users can give
    // verbose Windows device names short labels like "Desk" or "Headset".
    public Dictionary<string, string> OutputLabels { get; set; } = new(StringComparer.Ordinal);

    // Persist a device's Windows endpoint ID rather than its display name:
    // two monitors or headsets may legitimately report the same name. Values
    // retain their friendly names for the restore menu when disconnected.
    public Dictionary<string, string> HiddenOutputs { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Logical 96-DPI pixels; positive values move away from the notification area.</summary>
    public int WidgetOffset
    {
        get => _widgetOffset;
        set => _widgetOffset = Math.Clamp(value, 0, 2000);
    }
}
