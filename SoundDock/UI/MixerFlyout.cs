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
    private readonly Panel _outputDivider = new();
    private readonly Panel _appsDivider = new();
    private readonly Label _title = new();
    private readonly Button _close;
    private readonly Button _settings;
    private readonly Button _hiddenButton;
    private readonly ContextMenuStrip _deviceMenu = new();
    private readonly ContextMenuStrip _hiddenMenu = new();
    private readonly Label _deviceHeading = new();
    private readonly Label _appHeading = new();
    private readonly Label _emptyDevices = new();
    private readonly Label _emptySessions = new();
    private readonly Label _status = new();
    private readonly MixerRow _master;
    private readonly List<DeviceButton> _deviceButtons = [];
    private readonly Dictionary<string, MixerRow> _sessionRows = new(StringComparer.Ordinal);
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 10000, InitialDelay = 450 };
    private readonly Font _headingFont = new("Segoe UI", 9f, FontStyle.Bold);
    private readonly Font _titleFont = new("Segoe UI Semibold", 13.5f, FontStyle.Regular);
    private readonly Font _smallFont = new("Segoe UI", 9f);
    private readonly Font _bodyFont = new("Segoe UI", 9.5f);
    private AudioSnapshot _snapshot = AudioSnapshot.Empty();
    private IReadOnlyDictionary<string, string> _hiddenOutputs = new Dictionary<string, string>();
    private string _devicesKey = "";
    private string _sessionsKey = "";
    private string? _audioError;
    private DateTime _statusUntil;
    private Point _anchor;
    private int _naturalHeight;
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
        ClientSize = new Size(354, 340);
        Padding = new Padding(1);
        DoubleBuffered = true;

        _header.Dock = DockStyle.Top;
        _header.Height = 54;
        _footer.Dock = DockStyle.Bottom;
        _footer.Height = 42;
        _body.Dock = DockStyle.Fill;
        _body.AutoScroll = true;
        _body.TabStop = false;
        Controls.AddRange([_body, _footer, _header]);

        ConfigureLabel(_title, "Sound", Theme.Text, _titleFont);
        _close = new CenteredCloseButton();
        _close.Click += (_, _) => Dismiss();
        _header.Controls.AddRange([_title, _close]);

        _settings = new RoundedActionButton("More sound settings", "Open Windows sound settings")
        {
            ShowExternalArrow = true
        };
        _settings.Font = _smallFont;
        _settings.TextAlign = ContentAlignment.MiddleLeft;
        _settings.Click += (_, _) => SoundSettingsRequested?.Invoke();
        _footer.Controls.Add(_settings);
        _footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, S(18), 0, _footer.Width - S(18), 0);
        };

        ConfigureLabel(_deviceHeading, "Output", Theme.Muted, _headingFont);
        _hiddenButton = new RoundedActionButton("Hidden devices (0)", "Restore hidden sound outputs")
        {
            Font = _smallFont,
            ShowDropdownChevron = true,
            Visible = false,
        };
        _hiddenButton.Click += (_, _) => ShowHiddenMenu();
        _deviceMenu.Renderer = new DarkMenuRenderer();
        _deviceMenu.BackColor = Theme.Background;
        _deviceMenu.ForeColor = Theme.Text;
        _deviceMenu.ShowImageMargin = false;
        _deviceMenu.Opening += (_, e) =>
        {
            _deviceMenu.Items.Clear();
            if (_deviceMenu.SourceControl is not DeviceButton button)
            {
                e.Cancel = true;
                return;
            }
            var hide = new ToolStripMenuItem("Hide from output list", null,
                (_, _) => BeginInvoke(new Action(() => OutputHiddenRequested?.Invoke(button.Device.Id))));
            // Always keep the selected endpoint visible. Otherwise a user could
            // hide their sole active speaker and lose the means to switch back.
            hide.Enabled = !button.Device.IsDefault;
            hide.ToolTipText = button.Device.IsDefault
                ? "Switch to another output before hiding this one."
                : "Hide this output until you restore it from Hidden.";
            _deviceMenu.Items.Add(hide);
        };
        _hiddenMenu.Renderer = new DarkMenuRenderer();
        _hiddenMenu.BackColor = Theme.Background;
        _hiddenMenu.ForeColor = Theme.Text;
        _hiddenMenu.ShowImageMargin = false;
        ConfigureLabel(_appHeading, "App volume", Theme.Muted, _headingFont);
        ConfigureLabel(_emptyDevices, "No connected output devices", Theme.Muted, _smallFont);
        ConfigureLabel(_emptySessions, "Apps playing sound will appear here.", Theme.Muted, _smallFont);
        ConfigureLabel(_status, "", Theme.Text, _smallFont);
        _outputDivider.BackColor = Theme.Border;
        _appsDivider.BackColor = Theme.Border;
        _status.Visible = false;
        _status.AccessibleRole = AccessibleRole.StaticText;
        _master = new MixerRow("Volume", "", master: true);
        _master.TabIndex = 20;
        _master.VolumeChanged += value => MasterVolumeChanged?.Invoke(value);
        _master.MuteRequested += muted => MasterMuteRequested?.Invoke(muted);
        _body.Controls.AddRange([_deviceHeading, _hiddenButton, _appHeading, _emptyDevices, _emptySessions,
            _outputDivider, _appsDivider, _master, _status]);
        _hiddenButton.BringToFront(); // do not let the heading label intercept its clicks

        _body.SizeChanged += (_, _) => LayoutBody();
        // WinForms delivers wheel input to the focused child on some Windows
        // versions and to the hovered child on others. Routing once, by the
        // pointer location, makes the entire volume row respond consistently.
        Application.AddMessageFilter(this);
        Deactivate += (_, _) =>
        {
            // ToolStrip context menus are separate native popup windows. Do
            // not dismiss their owner while a hide/restore menu is displayed.
            if (!_opening && !_deviceMenu.Visible && !_hiddenMenu.Visible) Dismiss();
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
        if (endpointChanged) { _wheelRow = null; _wheelRemainder = 0; }
        _hiddenOutputs = hiddenOutputs ?? new Dictionary<string, string>();
        _hiddenButton.Visible = _hiddenOutputs.Count > 0;
        _hiddenButton.Text = $"Hidden devices ({_hiddenOutputs.Count})";
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
            if (changed) { LayoutBody(); FitToScreen(); }
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

    private void RebuildDevices(IReadOnlyList<AudioDevice> devices, string key)
    {
        _body.SuspendLayout();
        foreach (var control in _deviceButtons)
        {
            _toolTip.SetToolTip(control, null);
            control.ContextMenuStrip = null; // the shared menu belongs to this flyout
            _body.Controls.Remove(control);
            control.Dispose();
        }
        _deviceButtons.Clear();
        foreach (var device in devices)
        {
            var button = new DeviceButton(device);
            button.ContextMenuStrip = _deviceMenu;
            string id = device.Id;
            button.Click += (_, _) => OutputSelected?.Invoke(id);
            button.TabIndex = _deviceButtons.Count;
            _toolTip.SetToolTip(button, device.Name +
                (device.IsCommunicationsDefault ? "\nDefault output for calls" : "") +
                (device.IsDefault ? "" : "\nRight-click to hide this output"));
            _deviceButtons.Add(button);
            _body.Controls.Add(button);
        }
        _devicesKey = key;
        _body.ResumeLayout(false);
    }

    private void ShowHiddenMenu()
    {
        _hiddenMenu.Items.Clear();
        if (_hiddenOutputs.Count == 0) return;
        foreach (var hidden in _hiddenOutputs.OrderBy(item => item.Value,
                     StringComparer.CurrentCultureIgnoreCase))
        {
            string id = hidden.Key;
            string name = _snapshot.Devices.FirstOrDefault(device => device.Id == id)?.Name ?? hidden.Value;
            var item = new ToolStripMenuItem("Show " + name, null,
                (_, _) => BeginInvoke(new Action(() => OutputUnhiddenRequested?.Invoke(id))));
            item.ToolTipText = "Restore this output to the list";
            _hiddenMenu.Items.Add(item);
        }
        _hiddenMenu.Items.Add(new ToolStripSeparator());
        _hiddenMenu.Items.Add(new ToolStripMenuItem("Show all outputs", null,
            (_, _) => BeginInvoke(new Action(() => ShowAllOutputsRequested?.Invoke()))));
        _hiddenMenu.Show(_hiddenButton, new Point(0, _hiddenButton.Height));
    }

    /// <summary>Update master audio without rebuilding app sliders while dragging.</summary>
    public void ApplyMasterState(float volume, bool muted)
    {
        _master.ApplyValue(Percent(volume), muted);
    }

    private void RebuildSessions(IReadOnlyList<AudioSession> sessions, string key)
    {
        _wheelRow = null;
        _wheelRemainder = 0;
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
        }
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
        _anchor = anchor;
        _opening = true;
        try
        {
            // Place the hidden window on the correct monitor before Windows chooses its DPI.
            var work = Screen.FromPoint(anchor).WorkingArea;
            if (!Visible) Location = new Point(work.Right - S(364), work.Bottom - S(470));
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
        _wheelRow = null;
        _wheelRemainder = 0;
        _master.CancelInteraction();
        foreach (var row in _sessionRows.Values) row.CancelInteraction();
        Hide();
        Dismissed?.Invoke();
    }

    private void FitToScreen()
    {
        if (!IsHandleCreated) return;
        var work = Screen.FromPoint(_anchor).WorkingArea;
        int width = Math.Min(S(354), Math.Max(S(220), work.Width - S(24)));
        int maximumHeight = Math.Max(S(170), work.Height - S(24));
        int height = Math.Min(Math.Min(S(670), maximumHeight), Math.Max(S(190), _naturalHeight + S(100)));
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
        _header.Height = S(54);
        _footer.Height = S(42);
        _title.SetBounds(S(19), S(11), _header.Width - S(80), S(32));
        _close.SetBounds(_header.Width - S(43), S(10), S(29), S(29));
        _settings.SetBounds(S(14), S(5), S(214), S(31));
        LayoutBody();
    }

    private void LayoutBody()
    {
        if (_layoutRunning || _master is null || _body.ClientSize.Width < S(80)) return;
        _layoutRunning = true;
        _body.SuspendLayout();
        try
        {
            int margin = S(16);
            int width = Math.Max(S(180), _body.ClientSize.Width - 2 * margin);
            int scroll = _body.AutoScrollPosition.Y;
            int y = S(2);
            void Place(Control control, int height)
            {
                control.SetBounds(margin, y + scroll, width, height);
                y += height;
            }
            Place(_deviceHeading, S(24));
            // Control.Visible reports false when the flyout itself is hidden;
            // the saved hidden-device count is the reliable layout condition.
            if (_hiddenOutputs.Count > 0)
            {
                // Use the same 214x31 logical dimensions, shape and padding as
                // the footer's "More sound settings" action. Keeping this on
                // its own row avoids compressing it beside the Output heading.
                _hiddenButton.SetBounds(margin, y + scroll + S(5), Math.Min(S(214), width), S(31));
                y += S(42);
            }
            if (_deviceButtons.Count == 0) Place(_emptyDevices, S(40));
            foreach (var button in _deviceButtons) { Place(button, S(46)); y += S(2); }
            y += S(8);
            Place(_outputDivider, S(1));
            y += S(12);
            Place(_master, S(70));
            y += S(10);
            Place(_appsDivider, S(1));
            y += S(12);
            Place(_appHeading, S(24));
            if (_sessionRows.Count == 0) Place(_emptySessions, S(41));
            foreach (var row in _sessionRows.Values) { Place(row, S(65)); y += S(2); }
            if (_hasStatus)
            {
                y += S(8);
                int statusHeight = Math.Max(S(30), TextRenderer.MeasureText(_status.Text, _status.Font,
                    new Size(width, int.MaxValue), TextFormatFlags.WordBreak).Height + S(6));
                Place(_status, statusHeight);
            }
            y += S(8);
            _naturalHeight = y;
            _body.AutoScrollMinSize = new Size(0, y);
        }
        finally
        {
            _body.ResumeLayout(false);
            _layoutRunning = false;
        }
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
            _deviceMenu.Dispose();
            _hiddenMenu.Dispose();
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
    private bool _hovered;
    private readonly Font _nameFont = new("Segoe UI", 10f);

    public DeviceButton(AudioDevice device)
    {
        Device = device;
        Text = device.Name;
        AccessibleName = device.Name;
        AccessibleDescription = device.IsDefault ? "Current default sound output" : "Switch sound output to " + device.Name;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(Theme.Background);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int S(int value) => (int)Math.Round(value * DeviceDpi / 96f);
        if (Device.IsDefault || _hovered || Focused)
        {
            using var shape = Theme.RoundedRectangle(new RectangleF(1f, 1f, Width - 2f, Height - 2f), S(6));
            using var background = new SolidBrush(Device.IsDefault ? Theme.Selected : Theme.Hover);
            graphics.FillPath(background, shape);
        }
        if (Device.IsDefault)
        {
            using var selection = new SolidBrush(Theme.Accent);
            using var selectionPath = Theme.RoundedRectangle(new RectangleF(S(2), S(11), S(3), Height - S(22)), S(2));
            graphics.FillPath(selection, selectionPath);
        }
        var iconColor = Device.IsDefault ? Theme.Text : Theme.Muted;
        Theme.DrawFluentSpeaker(graphics,
            new RectangleF(S(12), (Height - S(21)) / 2f, S(21), S(21)), iconColor, false, 70);
        TextRenderer.DrawText(graphics, Device.Name, _nameFont,
            new Rectangle(S(42), 0, Math.Max(1, Width - S(78)), Height), Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        if (Device.IsDefault)
        {
            using var pen = new Pen(Theme.Text, Math.Max(1.5f, S(2)))
                { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            int x = Width - S(24), y = Height / 2;
            graphics.DrawLines(pen, [new Point(x - S(4), y), new Point(x, y + S(4)), new Point(x + S(7), y - S(5))]);
        }
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(graphics, ClientRectangle, Theme.Text, Theme.Background);
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
    private readonly Font _nameFont = new("Segoe UI", 9.5f);
    private readonly Font _smallFont = new("Segoe UI", 9f);
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
        _percentage.ForeColor = Theme.Muted;
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
        _percentage.ForeColor = muted ? Theme.Muted : Theme.Text;
        _mute.Muted = muted;
        _mute.AccessibleName = (muted ? "Unmute " : "Mute ") + _name.Text;
    }

    /// <summary>Adjust from a wheel step using the same event as dragging.</summary>
    public void AdjustVolume(int percentStep)
    {
        if (!Enabled || _slider.IsInteracting || percentStep == 0) return;
        _slider.Value = Math.Clamp(_slider.Value + percentStep, 0, 100);
    }

    public void CancelInteraction() { _slider.Capture = false; }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_slider is null) return;
        int S(int value) => (int)Math.Round(value * DeviceDpi / 96f);
        _name.SetBounds(S(8), S(3), Math.Max(S(40), Width - S(92)), S(23));
        _percentage.SetBounds(Width - S(74), S(3), S(64), S(23));
        int y = S(27);
        _mute.SetBounds(S(2), y, S(30), S(31));
        _slider.SetBounds(S(38), y, Math.Max(S(60), Width - S(42)), S(31));
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
