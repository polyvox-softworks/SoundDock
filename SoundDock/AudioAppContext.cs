using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using SoundDock.Audio;
using SoundDock.Infrastructure;
using SoundDock.UI;

namespace SoundDock;

internal sealed class AudioAppContext : ApplicationContext
{
    private readonly AudioService _audio = new();
    private readonly EndpointVolumeWatcher _volumeWatcher;
    private readonly AppSettings _settings = SettingsStore.Load();
    private readonly MixerFlyout _flyout = new();
    private readonly TaskbarWidget _widget = new();
    private readonly ContextMenuStrip _menu;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1500 };
    private readonly MessageWindow _messageWindow;
    private readonly ToolStripMenuItem _muteItem;
    private readonly ToolStripMenuItem _percentageItem;
    private readonly ToolStripMenuItem _renameOutputItem;
    private readonly ToolStripMenuItem _resetOutputItem;
    private readonly ToolStripMenuItem _callsItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _moveLeftItem;
    private readonly ToolStripMenuItem _moveRightItem;
    private readonly ToolStripMenuItem _resetPlacementItem;
    private readonly ToolStripMenuItem _placementStatusItem;
    private AudioSnapshot _snapshot = AudioSnapshot.Empty();
    private long _lastDismissedAt;
    private bool _widgetDisposed;
    private bool _disposed;

    internal AudioAppContext(bool background)
    {
        _settings.OutputLabels ??= new Dictionary<string, string>(StringComparer.Ordinal);
        _settings.HiddenOutputs ??= new Dictionary<string, string>(StringComparer.Ordinal);
        InitializeStartup();
        _messageWindow = new MessageWindow(ShowFlyout, RefreshWidgetPlacement,
            ShowErrorFlyout, HandleVolumeNotification, RefreshWindowsAccent);
        _volumeWatcher = new EndpointVolumeWatcher(_messageWindow.Handle);
        _menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            BackColor = Theme.Background,
            ForeColor = Theme.Text,
            ShowImageMargin = false,
            ShowCheckMargin = true,
            AccessibleName = "SoundDock menu"
        };

        var openItem = new ToolStripMenuItem("Open volume mixer", null,
            (_, _) => NativeMethods.PostMessage(_messageWindow.Handle,
                NativeMethods.ShowMixerMessage, IntPtr.Zero, IntPtr.Zero));
        _muteItem = new ToolStripMenuItem("Mute output", null, (_, _) => ToggleMasterMute());
        _percentageItem = new ToolStripMenuItem("Show output label and volume")
        {
            CheckOnClick = true,
            Checked = _settings.ShowPercentage
        };
        _percentageItem.Click += (_, _) =>
        {
            _settings.ShowPercentage = _percentageItem.Checked;
            UpdateWidget();
            SaveSettings();
        };
        _renameOutputItem = new ToolStripMenuItem("Rename active output label...", null, (_, _) => RenameActiveOutput());
        _resetOutputItem = new ToolStripMenuItem("Reset active output label", null, (_, _) => ResetActiveOutputLabel());
        _callsItem = new ToolStripMenuItem("Also switch call output")
        {
            CheckOnClick = true,
            Checked = _settings.SwitchCallOutput,
            ToolTipText = "Also change the Windows default communications playback device."
        };
        _callsItem.Click += (_, _) =>
        {
            _settings.SwitchCallOutput = _callsItem.Checked;
            SaveSettings();
        };
        _startupItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        _startupItem.Click += (_, _) => ChangeStartup();

        _moveLeftItem = new ToolStripMenuItem("Move left", null, (_, _) => ChangePlacement(8));
        _moveRightItem = new ToolStripMenuItem("Move right", null, (_, _) => ChangePlacement(-8));
        _resetPlacementItem = new ToolStripMenuItem("Reset position", null, (_, _) => SetPlacement(0));
        _placementStatusItem = new ToolStripMenuItem { Enabled = false, Visible = false };
        var placement = new ToolStripMenuItem("Position")
        {
            ToolTipText = "Adjust spacing beside other taskbar widgets."
        };
        placement.DropDownItems.AddRange([
            _moveLeftItem, _moveRightItem, new ToolStripSeparator(), _resetPlacementItem,
            _placementStatusItem
        ]);
        placement.DropDownOpening += (_, _) => RefreshPlacementItems();

        _menu.Items.AddRange([
            openItem, _muteItem,
            new ToolStripSeparator(),
            _percentageItem, _renameOutputItem, _resetOutputItem,
            placement, _callsItem, _startupItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Windows sound settings", null, (_, _) => OpenSoundSettings()),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit SoundDock", null, (_, _) => ExitThread())
        ]);
        _menu.Opening += MenuOpening;

        _widget.OpenRequested += WidgetOpenRequested;
        _widget.ToggleMuteRequested += ToggleMasterMute;
        _widget.MenuRequested += ShowMenu;
        _widget.VolumeStepRequested += AdjustMasterVolume;

        _flyout.OutputSelected += deviceId => RunAudioAction(() =>
            _audio.SetDefaultOutput(deviceId, _settings.SwitchCallOutput));
        _flyout.OutputHiddenRequested += HideOutput;
        _flyout.OutputUnhiddenRequested += RestoreOutput;
        _flyout.ShowAllOutputsRequested += RestoreAllOutputs;
        _flyout.MasterVolumeChanged += volume =>
        {
            if (!RunAudioAction(() => _audio.SetMasterVolume(volume / 100f), refresh: false)) return;
            _snapshot = _snapshot with { MasterVolume = volume / 100f };
            UpdateWidget();
        };
        _flyout.MasterMuteRequested += muted =>
            RunAudioAction(() => _audio.SetMasterMute(muted));
        _flyout.SessionVolumeChanged += (sessionId, volume) =>
            RunAudioAction(() => _audio.SetSessionVolume(sessionId, volume / 100f), refresh: false);
        _flyout.SessionMuteRequested += (sessionId, muted) =>
            RunAudioAction(() => _audio.SetSessionMute(sessionId, muted));
        _flyout.SoundSettingsRequested += OpenSoundSettings;
        _flyout.Dismissed += () => _lastDismissedAt = Environment.TickCount64;

        RefreshState();
        RefreshWidgetPlacement();

        _timer.Tick += (_, _) =>
        {
            RefreshWidgetPlacement();
            if (!_flyout.IsInteracting) RefreshState();
        };
        _timer.Start();

        if (!background && !_settings.HasOpened)
            Application.Idle += ShowFirstLaunch;
    }

    private void InitializeStartup()
    {
        if (!StartupRegistration.CanRegister) return;

        try
        {
            if (_settings.StartupInitialized)
            {
                StartupRegistration.RefreshExistingEntry();
                return;
            }

            StartupRegistration.SetEnabled(true);
            _settings.StartupInitialized = true;
            SettingsStore.Save(_settings);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            AppDiagnostics.Write(ex);
        }
    }

    private void ShowFirstLaunch(object? sender, EventArgs e)
    {
        Application.Idle -= ShowFirstLaunch;
        if (!_disposed) ShowFlyout();
    }

    private void WidgetOpenRequested(Point anchor)
    {
        if (_disposed) return;
        if (_flyout.Visible)
        {
            _flyout.Dismiss();
            return;
        }

        // Ignore the click that just dismissed the flyout.
        if (Environment.TickCount64 - _lastDismissedAt <= 250) return;
        ShowFlyoutAt(anchor);
    }

    private void ShowMenu(Point point)
    {
        if (_disposed || _widgetDisposed) return;
        _flyout.Dismiss();
        _menu.Show(point);
    }

    private void ShowFlyout()
    {
        if (_disposed || _widgetDisposed) return;
        RefreshWidgetPlacement();
        ShowFlyoutAt(_widget.PopupAnchor);
    }

    private void ShowFlyoutAt(Point anchor)
    {
        if (_disposed || _widgetDisposed) return;
        _menu.Close();
        RefreshState(includeSessions: true, forceApply: true);
        _flyout.ShowNear(anchor);
        if (!_settings.HasOpened)
        {
            _settings.HasOpened = true;
            SaveSettings();
        }
    }

    private void RefreshState(bool? includeSessions = null, bool forceApply = false)
    {
        if (_disposed) return;
        _snapshot = _audio.CaptureSnapshot(includeSessions ?? _flyout.Visible);
        _volumeWatcher.Watch(_snapshot.DefaultDeviceId);
        UpdateWidget();
        if ((forceApply || _flyout.Visible) && !_flyout.IsInteracting)
        {
            _flyout.ApplySnapshot(_snapshot, _settings.HiddenOutputs);
        }
    }

    private void HandleVolumeNotification(int scaledVolume, bool muted, int generation)
    {
        if (_disposed || generation != _volumeWatcher.Generation || !_snapshot.HasOutput) return;
        float volume = Math.Clamp(scaledVolume, 0, 10000) / 10000f;
        _snapshot = _snapshot with { MasterVolume = volume, MasterMuted = muted };
        UpdateWidget();
        if (_flyout.Visible) _flyout.ApplyMasterState(volume, muted);
    }

    private void RefreshWindowsAccent()
    {
        if (_disposed) return;
        if (!Theme.RefreshAccent()) return;
        _flyout.Invalidate(true);
        _menu.Invalidate(true);
    }

    private void HideOutput(string id)
    {
        var device = _snapshot.Devices.FirstOrDefault(d => d.Id == id);
        if (device is null || device.IsDefault) return;
        _settings.HiddenOutputs[id] = device.Name;
        _flyout.ApplySnapshot(_snapshot, _settings.HiddenOutputs);
        SaveSettings();
    }

    private void RestoreOutput(string id)
    {
        if (!_settings.HiddenOutputs.Remove(id)) return;
        _flyout.ApplySnapshot(_snapshot, _settings.HiddenOutputs);
        SaveSettings();
    }

    private void RestoreAllOutputs()
    {
        if (_settings.HiddenOutputs.Count == 0) return;
        _settings.HiddenOutputs.Clear();
        _flyout.ApplySnapshot(_snapshot, _settings.HiddenOutputs);
        SaveSettings();
    }

    private void UpdateWidget()
    {
        if (_disposed || _widgetDisposed) return;
        _widget.SetState(_snapshot, _settings.ShowPercentage, _settings.WidgetOffset,
            OutputLabel.For(_snapshot, _settings.OutputLabels));
    }

    private void RefreshWidgetPlacement()
    {
        if (_disposed || _widgetDisposed) return;
        _widget.RefreshPlacement();
    }

    private void ToggleMasterMute()
    {
        if (_disposed) return;
        RefreshState();
        if (!_snapshot.HasOutput)
        {
            ShowError("Connect an audio output before changing its volume.");
            return;
        }
        RunAudioAction(() => _audio.SetMasterMute(!_snapshot.MasterMuted));
    }

    private void AdjustMasterVolume(int percentStep)
    {
        if (_disposed || percentStep == 0) return;
        RefreshState();
        if (!_snapshot.HasOutput)
        {
            ShowError("Connect an audio output before changing its volume.");
            return;
        }
        float next = Math.Clamp(_snapshot.MasterVolume + Math.Clamp(percentStep, -100, 100) / 100f, 0f, 1f);
        RunAudioAction(() => _audio.SetMasterVolume(next));
    }

    private void RenameActiveOutput()
    {
        if (_snapshot.DefaultDeviceId is not { } id) return;
        string existing = _settings.OutputLabels.TryGetValue(id, out var custom)
            ? custom : OutputLabel.BaseName(_snapshot.DefaultDeviceName);
        string? response = OutputLabelDialog.Show(_snapshot.DefaultDeviceName ?? "Speakers", existing);
        if (response is null) return;
        if (string.IsNullOrWhiteSpace(response)) _settings.OutputLabels.Remove(id);
        else _settings.OutputLabels[id] = OutputLabel.Shorten(response);
        UpdateWidget();
        SaveSettings();
    }

    private void ResetActiveOutputLabel()
    {
        if (_snapshot.DefaultDeviceId is not { } id) return;
        if (!_settings.OutputLabels.Remove(id)) return;
        UpdateWidget();
        SaveSettings();
    }

    private void ChangePlacement(int adjustment) => SetPlacement(_settings.WidgetOffset + adjustment);

    private void SetPlacement(int offset)
    {
        _settings.WidgetOffset = offset;
        UpdateWidget();
        RefreshWidgetPlacement();
        RefreshPlacementItems();
        SaveSettings();
    }

    private void RefreshPlacementItems()
    {
        _moveLeftItem.Enabled = _settings.WidgetOffset < 2000;
        _moveRightItem.Enabled = _settings.WidgetOffset > 0;
        _resetPlacementItem.Enabled = _settings.WidgetOffset != 0;
        _placementStatusItem.Visible = !_widget.IsAttached && !string.IsNullOrEmpty(_widget.PlacementStatus);
        _placementStatusItem.Text = "Floating indicator is active";
        _placementStatusItem.ToolTipText = _widget.PlacementStatus ?? "";
    }

    private bool RunAudioAction(Action action, bool refresh = true)
    {
        if (_disposed) return false;
        try
        {
            action();
            if (refresh) RefreshState();
            return true;
        }
        catch (Exception ex) when (ex is AudioOperationException or COMException or InvalidOperationException)
        {
            AppDiagnostics.Write(ex);
            if (!_flyout.IsInteracting) RefreshState();
            ShowError(ex.Message);
            return false;
        }
    }

    private void MenuOpening(object? sender, CancelEventArgs e)
    {
        _flyout.Dismiss();
        RefreshState();
        _muteItem.Checked = _snapshot.MasterMuted;
        _muteItem.Enabled = _snapshot.HasOutput;
        _percentageItem.Checked = _settings.ShowPercentage;
        _renameOutputItem.Enabled = _snapshot.HasOutput;
        _resetOutputItem.Enabled = _snapshot.DefaultDeviceId is { } deviceId && _settings.OutputLabels.ContainsKey(deviceId);
        _callsItem.Checked = _settings.SwitchCallOutput;
        RefreshPlacementItems();
        RefreshStartupItem();
    }

    private void RefreshStartupItem()
    {
        try
        {
            _startupItem.Checked = StartupRegistration.IsEnabled;
            _startupItem.Enabled = StartupRegistration.CanRegister || _startupItem.Checked;
            _startupItem.ToolTipText = _startupItem.Enabled
                ? "Show the taskbar indicator when you sign in."
                : "Build the portable executable before enabling startup.";
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            AppDiagnostics.Write(ex);
            _startupItem.Checked = false;
            _startupItem.Enabled = false;
            _startupItem.ToolTipText = "Windows did not allow the startup setting to be read.";
        }
    }

    private void ChangeStartup()
    {
        try
        {
            StartupRegistration.SetEnabled(_startupItem.Checked);
            _settings.StartupInitialized = true;
            SaveSettings();
            RefreshStartupItem();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException or InvalidOperationException)
        {
            AppDiagnostics.Write(ex);
            RefreshStartupItem();
            ShowError("The startup preference could not be changed. " + ex.Message);
        }
    }

    private void SaveSettings()
    {
        try { SettingsStore.Save(_settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            AppDiagnostics.Write(ex);
            ShowError("Your preference applies now, but could not be saved for the next launch.");
        }
    }

    private void OpenSoundSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
            _flyout.Dismiss();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            AppDiagnostics.Write(ex);
            ShowError("Windows sound settings could not be opened. " + ex.Message);
        }
    }

    private void ShowError(string message)
    {
        if (_disposed || _widgetDisposed) return;
        if (_flyout.Visible)
            _flyout.ShowStatus(message, error: true);
        else if (!_messageWindow.PostError(message))
            ShowErrorFlyout(message);
    }

    private void ShowErrorFlyout(string message)
    {
        if (_disposed || _widgetDisposed) return;
        _menu.Close();
        RefreshState(includeSessions: true, forceApply: true);
        _flyout.ShowNear(_widget.PopupAnchor);
        _flyout.ShowStatus(message, error: true);
    }

    private void DisposeWidget()
    {
        if (_widgetDisposed) return;
        _widgetDisposed = true;
        _widget.Dispose();
    }

    protected override void ExitThreadCore()
    {
        Application.Idle -= ShowFirstLaunch;
        _timer.Stop();
        _volumeWatcher.Dispose();
        _menu.Close();
        _flyout.Dismiss();
        DisposeWidget();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            Application.Idle -= ShowFirstLaunch;
            _timer.Stop();
            _timer.Dispose();
            _volumeWatcher.Dispose();
            DisposeWidget();
            _menu.Dispose();
            _flyout.Dispose();
            _audio.Dispose();
            _messageWindow.Dispose();
        }
        base.Dispose(disposing);
    }

}
