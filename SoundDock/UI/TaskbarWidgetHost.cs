using SoundDock.Audio;

namespace SoundDock.UI;

/// <summary>Maintains one synchronized SoundDock widget on each chosen Windows taskbar.</summary>
internal sealed class TaskbarWidgetHost : IDisposable
{
    private readonly Dictionary<string, TaskbarWidget> _widgets = new(StringComparer.OrdinalIgnoreCase);
    private AudioSnapshot _snapshot = AudioSnapshot.Empty();
    private string _label = "Speakers";
    private string _mode = "All";
    private string _selectedDisplay = "";
    private string? _lastInteractedDisplay;
    private bool _showPercentage = true;
    private int _offset;
    private IReadOnlyDictionary<string, int> _displayOffsets = new Dictionary<string, int>();
    private bool _selectedFallback;
    private bool _disposed;

    internal event Action<Point>? OpenRequested;
    internal event Action<Point>? MenuRequested;
    internal event Action? ToggleMuteRequested;
    internal event Action<int>? VolumeStepRequested;

    internal bool IsAttached => _widgets.Values.Any(widget => widget.IsAttached);
    internal string ActiveDisplayName => _lastInteractedDisplay is { } previous &&
        Screen.AllScreens.Any(screen => string.Equals(screen.DeviceName, previous,
            StringComparison.OrdinalIgnoreCase)) ? previous : Screen.FromPoint(Cursor.Position).DeviceName;
    internal string? PlacementStatus => _widgets.Values.Select(widget => widget.PlacementStatus)
        .FirstOrDefault(status => !string.IsNullOrWhiteSpace(status));

    internal Point PopupAnchor
    {
        get
        {
            var screens = Screen.AllScreens;
            var underCursor = Screen.FromPoint(Cursor.Position).DeviceName;
            if (_widgets.TryGetValue(underCursor, out var current) && current.IsAttached)
                return current.PopupAnchor;
            if (_lastInteractedDisplay is { } last && _widgets.TryGetValue(last, out var recent))
                return recent.PopupAnchor;
            var primary = screens.FirstOrDefault(screen => screen.Primary);
            if (primary is not null && _widgets.TryGetValue(primary.DeviceName, out var primaryWidget))
                return primaryWidget.PopupAnchor;
            return _widgets.Values.FirstOrDefault()?.PopupAnchor ?? Cursor.Position;
        }
    }

    internal void ConfigureDisplays(string mode, string selectedDisplay)
    {
        if (_disposed) return;
        _mode = mode is "Primary" or "Selected" ? mode : "All";
        _selectedDisplay = selectedDisplay ?? "";
        _selectedFallback = false;
        RefreshPlacement();
    }

    internal void SetState(AudioSnapshot snapshot, bool showPercentage, int offset, string displayLabel,
        IReadOnlyDictionary<string, int> displayOffsets)
    {
        if (_disposed) return;
        _snapshot = snapshot;
        _showPercentage = showPercentage;
        _offset = offset;
        _displayOffsets = displayOffsets;
        _label = displayLabel;
        SyncWidgets();
        foreach (var entry in _widgets)
            entry.Value.SetState(_snapshot, _showPercentage, OffsetFor(entry.Key), _label);
    }

    internal void RefreshPlacement()
    {
        if (_disposed) return;
        SyncWidgets();
        foreach (var widget in _widgets.Values)
            widget.RefreshPlacement();

        if (_mode == "Selected")
        {
            bool selectedAttached = _widgets.TryGetValue(_selectedDisplay, out var selected) &&
                selected.IsAttached;
            bool needsFallback = !selectedAttached;
            if (needsFallback != _selectedFallback)
            {
                _selectedFallback = needsFallback;
                SyncWidgets();
                foreach (var widget in _widgets.Values)
                    widget.RefreshPlacement();
            }
        }
    }

    private void SyncWidgets()
    {
        var screens = Screen.AllScreens;
        var primary = screens.FirstOrDefault(screen => screen.Primary) ?? screens.FirstOrDefault();
        if (primary is null) return;

        // Only enabled Windows taskbars are targets. If a selected monitor is
        // removed or its secondary taskbar is disabled, fall back to primary.
        Screen[] candidates = _mode switch
        {
            "Primary" => new[] { primary },
            "Selected" => screens.Where(screen =>
                string.Equals(screen.DeviceName, _selectedDisplay, StringComparison.OrdinalIgnoreCase) &&
                (screen.Primary || TaskbarWindowLocator.FindForDisplay(screen.DeviceName) != IntPtr.Zero))
                .DefaultIfEmpty(primary).ToArray(),
            _ => screens.Where(screen => screen.Primary ||
                TaskbarWindowLocator.FindForDisplay(screen.DeviceName) != IntPtr.Zero).ToArray()
        };

        if (_mode == "Selected" && _selectedFallback &&
            !candidates.Any(screen => screen.Primary))
            candidates = [.. candidates, primary];

        var wanted = new HashSet<string>(candidates.Select(screen => screen.DeviceName),
            StringComparer.OrdinalIgnoreCase);
        foreach (var name in _widgets.Keys.Where(name => !wanted.Contains(name)).ToArray())
        {
            _widgets[name].Dispose();
            _widgets.Remove(name);
        }

        foreach (var screen in candidates)
        {
            if (_widgets.ContainsKey(screen.DeviceName)) continue;
            var widget = new TaskbarWidget(screen.DeviceName, screen.Primary);
            string displayName = screen.DeviceName;
            widget.OpenRequested += point =>
            {
                _lastInteractedDisplay = displayName;
                OpenRequested?.Invoke(point);
            };
            widget.MenuRequested += point =>
            {
                _lastInteractedDisplay = displayName;
                MenuRequested?.Invoke(point);
            };
            widget.ToggleMuteRequested += () => ToggleMuteRequested?.Invoke();
            widget.VolumeStepRequested += step => VolumeStepRequested?.Invoke(step);
            _widgets.Add(screen.DeviceName, widget);
            widget.SetState(_snapshot, _showPercentage, OffsetFor(displayName), _label);
        }
    }

    private int OffsetFor(string displayName)
    {
        if (_displayOffsets.TryGetValue(displayName, out int offset)) return offset;
        bool primary = Screen.AllScreens.Any(screen => screen.Primary &&
            string.Equals(screen.DeviceName, displayName, StringComparison.OrdinalIgnoreCase));
        return primary ? _offset : 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var widget in _widgets.Values) widget.Dispose();
        _widgets.Clear();
    }
}
