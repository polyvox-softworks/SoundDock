using System.ComponentModel;
using System.Drawing.Drawing2D;
using SoundDock.Audio;

namespace SoundDock.UI;

/// <summary>Compact, keyboard-accessible sound panel opened from the taskbar widget.</summary>
internal sealed partial class MixerFlyout : Form, IMessageFilter
{
    private readonly Panel _header = new();
    private readonly Panel _footer = new();
    private readonly Panel _body = new();
    private readonly FluentScrollBar _scrollBar = new();
    private readonly Panel _outputDivider = new();
    private readonly Panel _appsDivider = new();
    private readonly Label _title = new();
    private readonly Button _close;
    private readonly Button _settings;
    private readonly Button _hiddenButton;
    private readonly RoundedGroupPanel _hiddenPanel = new();
    private readonly List<HiddenRestoreButton> _restoreButtons = [];
    private readonly HiddenRestoreButton _restoreAllButton = new("Show all outputs");
    private readonly Label _hiddenHint = new();
    private readonly Label _deviceHeading = new();
    private readonly Label _volumeHeading = new();
    private readonly Label _appHeading = new();
    private readonly Label _emptyDevices = new();
    private readonly Label _emptySessions = new();
    private readonly Label _status = new();
    private readonly MixerRow _master;
    private readonly List<DeviceButton> _deviceButtons = [];
    private readonly Dictionary<string, MixerRow> _sessionRows = new(StringComparer.Ordinal);
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 10000, InitialDelay = 450 };
    private readonly Font _headingFont = new("Segoe UI", 8.5f);
    private readonly Font _titleFont = new("Segoe UI Semibold", 10f);
    private readonly Font _smallFont = new("Segoe UI", 8.5f);
    private readonly Font _bodyFont = new("Segoe UI", 9f);
    private AudioSnapshot _snapshot = AudioSnapshot.Empty();
    private IReadOnlyDictionary<string, string> _hiddenOutputs = new Dictionary<string, string>();
    private string _devicesKey = "";
    private string _sessionsKey = "";
    private string _hiddenKey = "";
    private bool _hiddenExpanded;
    private string? _audioError;
    private DateTime _statusUntil;
    private Point _anchor;
    private int _naturalHeight;
    private int _scrollOffset;
    private bool _scrollBarNeeded;
    private bool _layoutRunning;
    private bool _opening;
    private bool _hasStatus;

    public event Action<string>? OutputSelected;
    public event Action<string>? OutputHiddenRequested;
    public event Action<string>? OutputUnhiddenRequested;
    public event Action? ShowAllOutputsRequested;
    public event Action<int>? MasterVolumeChanged;
    public event Action<bool>? MasterMuteRequested;
    public event Action<string, int>? SessionVolumeChanged;
    public event Action<string, bool>? SessionMuteRequested;
    public event Action? SoundSettingsRequested;
    public event Action? Dismissed;

    public bool IsInteracting => _master.IsInteracting || _sessionRows.Values.Any(row => row.IsInteracting);

    public MixerFlyout()
    {
        Text = "Sound";
        AccessibleName = "Sound output and volume mixer";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        KeyPreview = true;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = _bodyFont;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(344, 340);
        Padding = new Padding(1);
        DoubleBuffered = true;

        _header.Dock = DockStyle.Top;
        _header.Height = 49;
        _header.BackColor = Theme.Background;
        _footer.Dock = DockStyle.Bottom;
        _footer.Height = 58;
        _footer.BackColor = Theme.Background;
        _body.Dock = DockStyle.Fill;
        _body.BackColor = Theme.Background;
        // Manage scrolling ourselves so Windows does not draw a bright native scrollbar.
        _body.AutoScroll = false;
        _body.TabStop = false;
        _scrollBar.Width = S(12);
        _scrollBar.Visible = false;
        _scrollBar.ScrollRequested += ScrollTo;
        _body.Controls.Add(_scrollBar);
        Controls.AddRange([_body, _footer, _header]);

        ConfigureLabel(_title, "Sound", Theme.Text, _titleFont);
        _close = new CenteredCloseButton();
        _close.Click += (_, _) => Dismiss();
        _header.Controls.AddRange([_title, _close]);

        _settings = new RoundedActionButton("More sound settings", "Open Windows sound settings")
        {
            ShowExternalArrow = true,
            ShowSettingsIcon = true
        };
        _settings.Font = _smallFont;
        _settings.TextAlign = ContentAlignment.MiddleLeft;
        _settings.Click += (_, _) => SoundSettingsRequested?.Invoke();
        _footer.Controls.Add(_settings);
        _footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 0, 0, _footer.Width, 0);
        };

        ConfigureLabel(_deviceHeading, "Output", Color.FromArgb(190, 190, 190), _headingFont);
        ConfigureLabel(_volumeHeading, "Volume", Color.FromArgb(190, 190, 190), _headingFont);
        _hiddenButton = new RoundedActionButton("Hidden devices (0)", "Restore hidden sound outputs")
        {
            Font = _smallFont,
            ShowDropdownChevron = true,
            Visible = false
        };
        _hiddenButton.Click += (_, _) =>
        {
            _hiddenExpanded = !_hiddenExpanded;
            _hiddenPanel.Visible = _hiddenExpanded;
            LayoutBody();
            FitToScreen();
        };

        _hiddenPanel.Visible = false;
        ConfigureLabel(_hiddenHint, "Right-click an inactive output to hide it.", Theme.Muted, _smallFont);
        _hiddenHint.TextAlign = ContentAlignment.MiddleLeft;
        _restoreAllButton.Click += (_, _) =>
            BeginInvoke(new Action(() => ShowAllOutputsRequested?.Invoke()));
        _hiddenPanel.Controls.AddRange([_restoreAllButton, _hiddenHint]);
        ConfigureLabel(_appHeading, "App volume", Color.FromArgb(190, 190, 190), _headingFont);
        ConfigureLabel(_emptyDevices, "No connected output devices", Theme.Muted, _smallFont);
        ConfigureLabel(_emptySessions, "Apps playing sound will appear here.", Theme.Muted, _smallFont);
        ConfigureLabel(_status, "", Theme.Text, _smallFont);
        _outputDivider.BackColor = Theme.Border;
        _appsDivider.BackColor = Theme.Border;
        _status.Visible = false;
        _status.AccessibleRole = AccessibleRole.StaticText;
        _master = new MixerRow("System volume", "", master: true);
        _master.TabIndex = 20;
        _master.VolumeChanged += value => MasterVolumeChanged?.Invoke(value);
        _master.MuteRequested += muted => MasterMuteRequested?.Invoke(muted);
        _body.Controls.AddRange([_deviceHeading, _hiddenButton, _hiddenPanel,
            _volumeHeading, _appHeading, _emptyDevices, _emptySessions,
            _outputDivider, _appsDivider, _master, _status]);
        _hiddenButton.BringToFront();
        _scrollBar.BringToFront();
        WatchFocus(_hiddenButton);
        WatchFocus(_restoreAllButton);
        WatchFocus(_master);

        _body.SizeChanged += (_, _) => LayoutBody();
        // Wheel input scrolls the output/app list, regardless of which child is focused.
        Application.AddMessageFilter(this);
        Deactivate += (_, _) =>
        {
            if (!_opening) Dismiss();
        };
    }

    private int S(int logicalPixels) => (int)Math.Round(logicalPixels * DeviceDpi / 96f);

    private static void ConfigureLabel(Label label, string text, Color color, Font font)
    {
        label.Text = text;
        label.ForeColor = color;
        label.BackColor = Color.Transparent;
        label.Font = font;
        label.AutoSize = false;
        label.UseMnemonic = false;
    }

    public void ApplySnapshot(AudioSnapshot snapshot, IReadOnlyDictionary<string, string>? hiddenOutputs = null)
    {
        bool endpointChanged = _snapshot.DefaultDeviceId != snapshot.DefaultDeviceId;
        if (endpointChanged)
        {
            _master.CancelInteraction();
            foreach (var row in _sessionRows.Values) row.CancelInteraction();
        }
        _snapshot = snapshot;
        _hiddenOutputs = hiddenOutputs ?? new Dictionary<string, string>();
        bool hiddenChanged = RefreshHiddenOutputs();
        _master.Enabled = snapshot.HasOutput;
        _master.Subtitle = snapshot.DefaultDeviceName ?? "No output available";
        _master.ApplyValue(Percent(snapshot.MasterVolume), snapshot.MasterMuted);

        // A session starting or ending should not remove a slider during a drag.
        if (!IsInteracting)
        {
            // Hidden endpoints stay hidden across sessions; the current default
            // is an exception so the selected output can never disappear.
            var visibleDevices = snapshot.Devices.Where(device =>
                device.IsDefault || !_hiddenOutputs.ContainsKey(device.Id)).ToArray();
            string devicesKey = string.Join("\n", visibleDevices.Select(device =>
                $"{device.Id}\t{device.Name}\t{device.IsDefault}\t{device.IsCommunicationsDefault}")) +
                "\nHidden:" + string.Join(";", _hiddenOutputs.Keys.OrderBy(id => id, StringComparer.Ordinal));
            string sessionsKey = (snapshot.DefaultDeviceId ?? "") + "\n" + string.Join("\n", snapshot.Sessions.Select(session => $"{session.Id}\t{session.Name}"));
            bool changed = devicesKey != _devicesKey || sessionsKey != _sessionsKey;
            if (devicesKey != _devicesKey) RebuildDevices(visibleDevices, devicesKey);
            if (sessionsKey != _sessionsKey) RebuildSessions(snapshot.Sessions, sessionsKey);
            if (changed || hiddenChanged) { LayoutBody(); FitToScreen(); }
        }

        foreach (var session in snapshot.Sessions)
            if (_sessionRows.TryGetValue(session.Id, out var row))
                row.ApplyValue(Percent(session.Volume), session.Muted);

        _emptyDevices.Visible = _deviceButtons.Count == 0;
        _emptySessions.Visible = _sessionRows.Count == 0;
        _audioError = snapshot.Error;
        if (DateTime.UtcNow >= _statusUntil)
            SetStatusText(_audioError ?? "", error: _audioError is not null);
    }

    private static int Percent(float volume) => (int)Math.Round(Math.Clamp(volume, 0f, 1f) * 100);

    private bool RefreshHiddenOutputs()
    {
        string key = string.Join("\n", _hiddenOutputs.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => item.Key + "\t" + item.Value));
        if (key == _hiddenKey) return false;

        _hiddenKey = key;
        _hiddenButton.Visible = _hiddenOutputs.Count > 0;
        _hiddenButton.Text = $"Hidden devices ({_hiddenOutputs.Count})";
        if (_hiddenOutputs.Count == 0) _hiddenExpanded = false;
        _hiddenPanel.Visible = _hiddenExpanded;

        foreach (var button in _restoreButtons)
        {
            _hiddenPanel.Controls.Remove(button);
            button.Dispose();
        }
        _restoreButtons.Clear();

        foreach (var entry in _hiddenOutputs.OrderBy(item => item.Value,
                     StringComparer.CurrentCultureIgnoreCase))
        {
            string id = entry.Key;
            string name = _snapshot.Devices.FirstOrDefault(device => device.Id == id)?.Name ?? entry.Value;
            var button = new HiddenRestoreButton("Show " + name);
            button.AccessibleName = "Restore " + name;
            button.Click += (_, _) => BeginInvoke(new Action(() => OutputUnhiddenRequested?.Invoke(id)));
            _hiddenPanel.Controls.Add(button);
            _restoreButtons.Add(button);
            WatchFocus(button);
        }
        _restoreAllButton.Visible = _restoreButtons.Count > 1;
        return true;
    }

    private void RebuildDevices(IReadOnlyList<AudioDevice> devices, string key)
    {
        _body.SuspendLayout();
        foreach (var control in _deviceButtons)
        {
            _toolTip.SetToolTip(control, null);
            _body.Controls.Remove(control);
            control.Dispose();
        }
        _deviceButtons.Clear();
        foreach (var device in devices)
        {
            string id = device.Id;
            var button = new DeviceButton(device);
            button.Click += (_, _) => OutputSelected?.Invoke(id);
            button.HideRequested += () =>
            {
                // Rebuild the list after the mouse event has finished.
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke(new Action(() => OutputHiddenRequested?.Invoke(id)));
            };
            button.TabIndex = _deviceButtons.Count;
            _toolTip.SetToolTip(button, device.Name +
                (device.IsDefault ? "\nActive output. Switch first to hide it." : "\nRight-click to hide immediately."));
            _deviceButtons.Add(button);
            _body.Controls.Add(button);
            WatchFocus(button);
        }
        _scrollBar.BringToFront();
        _devicesKey = key;
        _body.ResumeLayout(false);
    }

    private int HiddenPanelHeight()
    {
        int count = _restoreButtons.Count;
        if (count == 0) return 0;
        int height = S(16) + count * S(31) + (count - 1) * S(3) + S(29);
        if (count > 1) height += S(34);
        return height;
    }

    private void LayoutHiddenPanel()
    {
        int padding = S(8);
        int width = Math.Max(S(80), _hiddenPanel.Width - 2 * padding);
        int y = padding;
        foreach (var button in _restoreButtons)
        {
            button.SetBounds(padding, y, width, S(31));
            y += S(34);
        }
        if (_restoreAllButton.Visible)
        {
            _restoreAllButton.SetBounds(padding, y, width, S(31));
            y += S(34);
        }
        _hiddenHint.SetBounds(padding + S(3), y, width - S(6), S(25));
    }

    /// <summary>Update master audio without rebuilding app sliders while dragging.</summary>
    public void ApplyMasterState(float volume, bool muted)
    {
        _master.ApplyValue(Percent(volume), muted);
    }

    private void RebuildSessions(IReadOnlyList<AudioSession> sessions, string key)
    {
        _body.SuspendLayout();
        foreach (var row in _sessionRows.Values)
        {
            _toolTip.SetToolTip(row, null);
            _body.Controls.Remove(row);
            row.Dispose();
        }
        _sessionRows.Clear();
        foreach (var session in sessions)
        {
            string id = session.Id;
            var row = new MixerRow(session.Name, "", master: false) { TabIndex = 30 + _sessionRows.Count };
            row.VolumeChanged += value => SessionVolumeChanged?.Invoke(id, value);
            row.MuteRequested += muted => SessionMuteRequested?.Invoke(id, muted);
            row.ApplyValue(Percent(session.Volume), session.Muted);
            _toolTip.SetToolTip(row, session.Name + (session.ProcessId == 0 ? "" : $" · Process {session.ProcessId}"));
            _sessionRows.Add(id, row);
            _body.Controls.Add(row);
            WatchFocus(row);
        }
        _scrollBar.BringToFront();
        _sessionsKey = key;
        _body.ResumeLayout(false);
    }

    public void ShowStatus(string text, bool error = false)
    {
        _statusUntil = DateTime.UtcNow.AddSeconds(error ? 12 : 5);
        SetStatusText(text, error);
    }

    private void SetStatusText(string text, bool error)
    {
        bool hasText = !string.IsNullOrEmpty(text);
        if (_status.Text == text && _hasStatus == hasText) return;
        _status.Text = text;
        _status.ForeColor = error ? Theme.Error : Theme.Muted;
        _hasStatus = hasText;
        _status.Visible = hasText;
        LayoutBody();
        FitToScreen();
    }

    public void ShowNear(Point anchor)
    {
        _scrollRemainder = 0;
        _anchor = anchor;
        _opening = true;
        try
        {
            // Place the hidden window on the correct monitor before Windows chooses its DPI.
            var work = Screen.FromPoint(anchor).WorkingArea;
            if (!Visible) Location = new Point(work.Right - S(354), work.Bottom - S(470));
            _ = Handle;
            LayoutBody();
            FitToScreen();
            Show();
            Activate();
            if (_deviceButtons.Count > 0)
                (_deviceButtons.FirstOrDefault(button => button.Device.IsDefault) ?? _deviceButtons[0]).Focus();
            else
                _close.Focus();
        }
        finally { _opening = false; }
    }

    public void Dismiss()
    {
        if (!Visible) return;
        _scrollRemainder = 0;
        _master.CancelInteraction();
        foreach (var row in _sessionRows.Values) row.CancelInteraction();
        Hide();
        Dismissed?.Invoke();
    }

    private void FitToScreen()
    {
        if (!IsHandleCreated) return;
        var work = Screen.FromPoint(_anchor).WorkingArea;
        int width = Math.Min(S(344), Math.Max(S(220), work.Width - S(24)));
        int maximumHeight = Math.Max(S(170), work.Height - S(24));
        int height = Math.Min(Math.Min(S(670), maximumHeight), Math.Max(S(190), _naturalHeight + S(107)));
        if (ClientSize != new Size(width, height)) ClientSize = new Size(width, height);
        int x = Math.Clamp(_anchor.X - Width + S(28), work.Left + S(8), Math.Max(work.Left + S(8), work.Right - Width - S(8)));
        int preferredY = _anchor.Y - Height - S(10);
        if (preferredY < work.Top) preferredY = _anchor.Y + S(10);
        int y = Math.Clamp(preferredY, work.Top + S(8), Math.Max(work.Top + S(8), work.Bottom - Height - S(8)));
        Location = new Point(x, y);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_close is null || _master is null) return;
        _header.Height = S(49);
        _footer.Height = S(58);
        _title.SetBounds(S(18), S(7), _header.Width - S(76), S(35));
        _close.SetBounds(_header.Width - S(40), S(10), S(29), S(29));
        _settings.SetBounds(S(17), S(11), Math.Max(S(110), _footer.Width - S(34)), S(32));
        LayoutBody();
    }

    private void LayoutBody()
    {
        if (_layoutRunning || _master is null || _body.ClientSize.Width < S(80)) return;
        _layoutRunning = true;
        _body.SuspendLayout();
        try
        {
            LayoutContent(_scrollBarNeeded);
            bool needed = _naturalHeight > _body.ClientSize.Height;
            if (needed != _scrollBarNeeded)
            {
                _scrollBarNeeded = needed;
                LayoutContent(needed);
            }

            _scrollBar.SetBounds(Math.Max(0, _body.ClientSize.Width - _scrollBar.Width),
                0, _scrollBar.Width, _body.ClientSize.Height);
            _scrollBar.Visible = needed;
            int limit = Math.Max(0, _naturalHeight - _body.ClientSize.Height);
            if (_scrollOffset > limit) ScrollTo(limit);
            _scrollBar.UpdateRange(_naturalHeight, _body.ClientSize.Height, _scrollOffset);
            _scrollBar.BringToFront();
        }
        finally
        {
            _body.ResumeLayout(false);
            _layoutRunning = false;
        }
    }

    private void LayoutContent(bool reserveScrollBar)
    {
        int margin = S(18);
        int available = _body.ClientSize.Width - (reserveScrollBar ? _scrollBar.Width : 0);
        int width = Math.Max(S(175), available - 2 * margin);
        int y = S(5);
        void Place(Control control, int height)
        {
            control.SetBounds(margin, y - _scrollOffset, width, height);
            y += height;
        }

        Place(_deviceHeading, S(26));
        if (_deviceButtons.Count == 0) Place(_emptyDevices, S(40));
        foreach (var button in _deviceButtons) { Place(button, S(36)); y += S(2); }
        if (_hiddenOutputs.Count > 0)
        {
            y += S(8);
            Place(_hiddenButton, S(32));
            if (_hiddenExpanded)
            {
                y += S(5);
                Place(_hiddenPanel, HiddenPanelHeight());
                LayoutHiddenPanel();
            }
        }
        y += S(13);
        Place(_outputDivider, S(1));
        y += S(13);
        Place(_volumeHeading, S(25));
        Place(_master, S(52));
        y += S(13);
        Place(_appsDivider, S(1));
        y += S(12);
        Place(_appHeading, S(26));
        if (_sessionRows.Count == 0) Place(_emptySessions, S(41));
        foreach (var row in _sessionRows.Values) { Place(row, S(62)); y += S(5); }
        if (_hasStatus)
        {
            y += S(8);
            int statusHeight = Math.Max(S(30), TextRenderer.MeasureText(_status.Text, _status.Font,
                new Size(width, int.MaxValue), TextFormatFlags.WordBreak).Height + S(6));
            Place(_status, statusHeight);
        }
        _naturalHeight = y + S(8);
    }

    private void ScrollTo(int requestedOffset)
    {
        int maximum = Math.Max(0, _naturalHeight - _body.ClientSize.Height);
        int next = Math.Clamp(requestedOffset, 0, maximum);
        int delta = next - _scrollOffset;
        if (delta == 0) return;
        _scrollOffset = next;
        _body.SuspendLayout();
        try
        {
            foreach (Control control in _body.Controls)
                if (!ReferenceEquals(control, _scrollBar))
                    control.Top -= delta;
        }
        finally { _body.ResumeLayout(false); }
        _scrollBar.SetScrollPosition(_scrollOffset);
    }

    // Keep Tab navigation useful when the list is taller than the flyout.
    private void WatchFocus(Control control)
    {
        if (control.TabStop)
            control.Enter += (_, _) =>
            {
                // Only keyboard focus moves the view; mouse clicks must not shift a slider.
                if (IsHandleCreated && Control.MouseButtons == MouseButtons.None)
                    BeginInvoke(new Action(() =>
                    {
                        if (!control.IsDisposed && control.Focused)
                            ScrollControlIntoView(control);
                    }));
            };
        foreach (Control child in control.Controls)
            WatchFocus(child);
    }

    private void ScrollControlIntoView(Control control)
    {
        if (!Visible || _layoutRunning || _naturalHeight <= _body.ClientSize.Height) return;
        int top = _body.PointToClient(control.PointToScreen(Point.Empty)).Y;
        int bottom = top + control.Height;
        int padding = S(5);
        if (top < padding)
            ScrollTo(_scrollOffset + top - padding);
        else if (bottom > _body.ClientSize.Height - padding)
            ScrollTo(_scrollOffset + bottom - _body.ClientSize.Height + padding);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Dismiss(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int roundCorners = 2;
        int dark = 1;
        // These attributes are optional visual enhancements; older Windows ignores them.
        NativeMethods.DwmSetWindowAttribute(Handle, 33, ref roundCorners, sizeof(int));
        NativeMethods.DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        _scrollBar.Width = S(12);
        LayoutBody();
        FitToScreen();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width < 4 || Height < 4) return;
        using var path = Theme.RoundedRectangle(new RectangleF(0, 0, Width, Height), S(12));
        var previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Theme.Border);
        using var path = Theme.RoundedRectangle(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), S(12));
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Dismiss();
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Application.RemoveMessageFilter(this);
            _toolTip.Dispose();
        }
        base.Dispose(disposing);
        if (disposing)
        {
            _headingFont.Dispose();
            _titleFont.Dispose();
            _smallFont.Dispose();
            _bodyFont.Dispose();
        }
    }
}

internal sealed class DeviceButton : Button
{
    public AudioDevice Device { get; }
    public event Action? HideRequested;
    private bool _hovered;
    private readonly Font _nameFont = new("Segoe UI", 9f);

    public DeviceButton(AudioDevice device)
    {
        Device = device;
        Text = device.Name;
        AccessibleName = device.Name;
        AccessibleDescription = device.IsDefault
            ? "Current output. Select another output before hiding this one."
            : "Click to switch output, or right-click to hide it.";
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) return;
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            if (!Device.IsDefault) HideRequested?.Invoke();
            return;
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int S(int value) => (int)Math.Round(value * DeviceDpi / 96f);
        if (Device.IsDefault || _hovered || Focused)
        {
            using var path = Theme.RoundedRectangle(new RectangleF(0, 0, Width - 1, Height - 1), S(5));
            using var fill = new SolidBrush(Device.IsDefault ? Theme.Selected : Color.FromArgb(53, 53, 53));
            g.FillPath(fill, path);
        }
        Theme.DrawFluentSpeaker(g,
            new RectangleF(S(10), (Height - S(17)) / 2f, S(17), S(17)),
            Theme.Text, false, 70);
        TextRenderer.DrawText(g, Device.Name, _nameFont,
            new Rectangle(S(39), 0, Math.Max(1, Width - S(77)), Height), Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        if (Device.IsDefault)
        {
            using var pen = new Pen(Theme.Accent, Math.Max(1.5f, S(1)))
            { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            int x = Width - S(21), y = Height / 2;
            g.DrawLines(pen, [new Point(x - S(4), y),
                new Point(x - S(1), y + S(3)), new Point(x + S(6), y - S(5))]);
        }
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(g, ClientRectangle, Theme.Text, Theme.Background);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _nameFont.Dispose();
    }
}

internal sealed class MixerRow : Panel
{
    private readonly Label _name = new();
    private readonly Label _percentage = new();
    private readonly VolumeSlider _slider = new();
    private readonly MuteButton _mute = new();
    private readonly Font _nameFont = new("Segoe UI", 9f);
    private readonly Font _smallFont = new("Segoe UI", 8.5f);
    private string _subtitle = "";

    public event Action<int>? VolumeChanged;
    public event Action<bool>? MuteRequested;
    public bool IsInteracting => _slider.IsInteracting;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Subtitle { get => _subtitle; set => _subtitle = value; }

    public MixerRow(string name, string subtitle, bool master)
    {
        _subtitle = subtitle;
        DoubleBuffered = true;
        BackColor = Theme.Background;
        _name.Text = name;
        _name.Font = _nameFont;
        _name.ForeColor = Theme.Text;
        _name.AutoEllipsis = true;
        _name.UseMnemonic = false;
        _percentage.Font = _smallFont;
        _percentage.ForeColor = Color.FromArgb(201, 201, 201);
        _percentage.TextAlign = ContentAlignment.MiddleRight;
        _slider.AccessibleName = name + " volume";
        _mute.AccessibleName = "Mute " + name;
        _slider.ValueChanged += (_, _) =>
        {
            _percentage.Text = _slider.Value + "%";
            VolumeChanged?.Invoke(_slider.Value);
        };
        _mute.Click += (_, _) => MuteRequested?.Invoke(!_mute.Muted);
        Controls.AddRange([_name, _percentage, _slider, _mute]);
        _mute.TabIndex = 0;
        _slider.TabIndex = 1;
    }

    public void ApplyValue(int value, bool muted)
    {
        _slider.SetValueSilently(value);
        _percentage.Text = (_slider.IsInteracting ? _slider.Value : value) + "%";
        _percentage.ForeColor = muted ? Theme.Muted : Color.FromArgb(201, 201, 201);
        _mute.Muted = muted;
        _mute.AccessibleName = (muted ? "Unmute " : "Mute ") + _name.Text;
    }

    public void CancelInteraction() { _slider.Capture = false; }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_slider is null) return;
        int S(int value) => (int)Math.Round(value * DeviceDpi / 96f);
        _mute.SetBounds(0, S(1), S(22), S(23));
        _name.SetBounds(S(28), S(1), Math.Max(S(30), Width - S(103)), S(23));
        _percentage.SetBounds(Width - S(65), S(1), S(65), S(23));
        _slider.SetBounds(0, S(27), Math.Max(S(60), Width), S(24));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { _nameFont.Dispose(); _smallFont.Dispose(); }
    }
}

internal sealed class MuteButton : Button
{
    private bool _muted;
    private bool _hovered;
    [DefaultValue(false)]
    public bool Muted { get => _muted; set { _muted = value; Invalidate(); } }

    public MuteButton()
    {
        AccessibleName = "Mute";
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        // The previous custom button used a different outlined glyph and did
        // not clear its paint surface; volume/mute transitions could leave
        // behind part of the previous glyph on some WinForms themes.
        e.Graphics.Clear(Theme.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        if (_hovered || Focused)
        {
            using var background = new SolidBrush(Theme.Hover);
            using var shape = Theme.RoundedRectangle(new RectangleF(0, 0, Width - 1, Height - 1), 6 * DeviceDpi / 96f);
            e.Graphics.FillPath(background, shape);
        }
        float size = Math.Min(Width, Height) * .76f;
        Theme.DrawFluentSpeaker(e.Graphics,
            new RectangleF((Width - size) / 2, (Height - size) / 2, size, size),
            !Enabled ? Theme.Border : Muted ? Theme.Muted : Theme.Text,
            Muted, Muted ? 0 : 70);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, Theme.Muted, Theme.Surface);
    }
}
