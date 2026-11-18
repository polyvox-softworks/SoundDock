using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using SoundDock.Audio;
using SoundDock.Infrastructure;

namespace SoundDock.UI;

/// <summary>
/// An app-owned layered child of Explorer's primary taskbar. It is deliberately
/// separate from Shell_NotifyIcon and can never enter the notification overflow.
/// Explorer's window classes are implementation details, so a visible floating
/// control is used when the expected taskbar hierarchy is not available.
/// </summary>
internal sealed class TaskbarWidget : IDisposable
{
    private WidgetWindow? _window;
    private IntPtr _parent;
    private IntPtr _tooltip;
    private IntPtr _tooltipText;
    private Rectangle _bounds;
    private AudioSnapshot _snapshot = AudioSnapshot.Empty();
    private string _lastTooltip = "";
    private bool _showPercentage = true;
    private string _displayLabel = "Speakers";
    private bool _dark = true;
    private bool _vertical;
    private bool _hovered;
    private bool _floating;
    private bool _needsRender = true;
    private bool _refreshing;
    private bool _disposed;
    private int _offset;
    private int _dpi = 96;
    private int _wheelRemainder;
    private DateTime _lastFailureLog;

    public event Action<Point>? OpenRequested;
    public event Action? ToggleMuteRequested;
    public event Action<Point>? MenuRequested;
    public event Action<int>? VolumeStepRequested;

    public bool IsAttached => !_floating && _window is { Handle: var hwnd } &&
                              hwnd != IntPtr.Zero && WidgetNative.IsWindow(hwnd) &&
                              WidgetNative.IsWindow(_parent);
    public string? PlacementStatus { get; private set; }

    public Point PopupAnchor
    {
        get
        {
            using var awareness = WidgetDpiScope.PerMonitor();
            if (_window is { Handle: var hwnd } && WidgetNative.GetWindowRect(hwnd, out var rectangle))
            {
                var screen = Screen.FromHandle(hwnd);
                var bounds = rectangle.ToRectangle();
                bool top = bounds.Bottom <= screen.WorkingArea.Top + S(8);
                return new Point(bounds.Right - S(8), top ? bounds.Bottom : bounds.Top);
            }
            var work = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;
            return new Point(work.Right - S(20), work.Bottom - S(10));
        }
    }

    private int S(int value) => (int)Math.Round(value * _dpi / 96f);
    private static int Percent(float value) => (int)Math.Round(Math.Clamp(value, 0f, 1f) * 100f);

    public void SetState(AudioSnapshot snapshot, bool showPercentage, int offset, string displayLabel)
    {
        if (_disposed) return;
        displayLabel = string.IsNullOrWhiteSpace(displayLabel) ? "No output" : OutputLabel.Shorten(displayLabel.Trim());
        bool geometryChanged = _showPercentage != showPercentage || _offset != Math.Clamp(offset, 0, 2000) ||
                               !string.Equals(_displayLabel, displayLabel, StringComparison.Ordinal);
        _needsRender |= Percent(_snapshot.MasterVolume) != Percent(snapshot.MasterVolume) ||
                        _snapshot.MasterMuted != snapshot.MasterMuted ||
                        _snapshot.HasOutput != snapshot.HasOutput || geometryChanged;
        _snapshot = snapshot;
        _showPercentage = showPercentage;
        _displayLabel = displayLabel;
        _offset = Math.Clamp(offset, 0, 2000);
        if (geometryChanged || _window is null || !WidgetNative.IsWindow(_window.Handle))
            RefreshPlacement();
        else
            RenderAndUpdateTooltip();
    }

    public void RefreshPlacement()
    {
        if (_disposed || _refreshing) return;
        _refreshing = true;
        try
        {
            using var awareness = WidgetDpiScope.PerMonitor();
            bool dark = Theme.IsDarkTaskbar();
            _needsRender |= dark != _dark;
            _dark = dark;
            string reason;
            try
            {
                if (TryAttach(out reason)) return;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ExternalException)
            {
                LogPlacementFailure(ex);
                reason = "Taskbar attachment unavailable; showing a floating volume control.";
            }
            ShowFloating(reason);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ExternalException)
        {
            LogPlacementFailure(ex);
            PlacementStatus = "Widget unavailable. Run SoundDock again to open the mixer.";
        }
        finally { _refreshing = false; }
    }

    private bool TryAttach(out string reason)
    {
        reason = "Taskbar not available; showing a floating volume control.";
        IntPtr taskbar = WidgetNative.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !WidgetNative.GetWindowRect(taskbar, out var taskbarNative)) return false;
        Rectangle taskbarBounds = taskbarNative.ToRectangle();
        if (taskbarBounds.Width <= 0 || taskbarBounds.Height <= 0) return false;

        IntPtr notification = FindNotificationWindow(taskbar);
        if (notification == IntPtr.Zero || !WidgetNative.GetWindowRect(notification, out var notificationNative))
        {
            reason = "This taskbar layout is not recognized; showing a floating volume control.";
            return false;
        }

        bool vertical = taskbarBounds.Width < taskbarBounds.Height;
        int dpi = checked((int)WidgetNative.GetDpiForWindow(taskbar));
        if (dpi <= 0) dpi = 96;
        _needsRender |= dpi != _dpi || vertical != _vertical;
        _dpi = dpi;
        _vertical = vertical;
        // The reference battery indicators fill most of a 48-pixel taskbar.
        // Size from the actual label so "Desk" stays compact while a monitor
        // name gets enough room. Never stretch a small rendered bitmap.
        var size = DesiredSize(taskbarBounds, vertical);
        var obstacles = FindOtherWidgets(taskbar, taskbarBounds);
        var bounds = TaskbarPlacement.Calculate(taskbarBounds, notificationNative.ToRectangle(), obstacles,
            size, S(4), S(_offset));
        if (bounds.IsEmpty)
        {
            reason = "No clear taskbar space; showing a floating volume control. Try resetting Position.";
            return false;
        }

        EnsureWindow(taskbar);
        _floating = false;
        PlacementStatus = null;
        SetBounds(bounds, taskbar);
        RenderAndUpdateTooltip(throwOnFailure: true);
        return true;
    }

    private void ShowFloating(string reason)
    {
        // A fallback stays outside the hidden-icon menu. No NotifyIcon is created.
        bool wasFloating = _floating;
        _floating = true;
        _vertical = false;
        _needsRender |= !wasFloating;
        int dpi = checked((int)WidgetNative.GetDpiForSystem());
        _needsRender |= _dpi != dpi;
        _dpi = dpi > 0 ? dpi : 96;
        EnsureWindow(IntPtr.Zero);
        PlacementStatus = reason;
        var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
        var work = screen.WorkingArea;
        var size = DesiredSize(new Rectangle(0, 0, S(400), S(48)), vertical: false);
        int x = Math.Max(work.Left + S(8), work.Right - size.Width - S(12) - S(_offset));
        var bounds = new Rectangle(x, Math.Max(work.Top + S(8), work.Bottom - size.Height - S(10)), size.Width, size.Height);
        bool suppressed = ForegroundCoversScreen(screen.Bounds);
        if (suppressed) WidgetNative.ShowWindow(_window!.Handle, 0);
        // Do not show and immediately hide on every polling tick; that can flash
        // a floating fallback over a fullscreen application.
        SetBounds(bounds, IntPtr.Zero, show: !suppressed);
        RenderAndUpdateTooltip(throwOnFailure: true);
    }

    private Size DesiredSize(Rectangle taskbar, bool vertical)
    {
        if (vertical)
            return new Size(Math.Max(1, Math.Min(taskbar.Width - S(2), S(46))),
                S(_showPercentage ? 73 : 40));
        int height = Math.Max(1, Math.Min(taskbar.Height - S(2), S(42)));
        if (!_showPercentage) return new Size(S(40), height);
        using var font = new Font("Segoe UI", S(13), FontStyle.Regular, GraphicsUnit.Pixel);
        int measured = TextRenderer.MeasureText(_displayLabel, font,
            new Size(S(1000), S(28)), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
        // Icon (23), leading (8), icon/text gap (7), and trailing (9).
        int width = Math.Clamp(S(47) + measured, S(97), S(196));
        return new Size(width, height);
    }

    private void EnsureWindow(IntPtr parent)
    {
        if (_window is not null && WidgetNative.IsWindow(_window.Handle) && _parent == parent) return;
        DestroyWindow();
        _parent = parent;
        using var awareness = parent == IntPtr.Zero ? WidgetDpiScope.PerMonitor() : WidgetDpiScope.ForWindow(parent);
        var window = new WidgetWindow(this);
        try
        {
            window.CreateHandle(new CreateParams
            {
                Caption = "SoundDock volume control",
                Parent = parent,
                Style = parent != IntPtr.Zero ? 0x40000000 | 0x04000000 : unchecked((int)0x80000000),
                ExStyle = 0x00080000 | 0x08000000 | 0x00000080,
                Width = 1,
                Height = 1
            });
            _window = window;
        }
        catch
        {
            window.DestroyHandle();
            throw;
        }
        _hovered = false;
        _needsRender = true;
        _lastTooltip = "";
        CreateTooltip();
    }

    private void SetBounds(Rectangle bounds, IntPtr parent, bool show = true)
    {
        if (_window is null) return;
        _needsRender |= _bounds.Size != bounds.Size;
        _bounds = bounds;
        var position = new WidgetNative.NativePoint(bounds.Left, bounds.Top);
        if (parent != IntPtr.Zero && !WidgetNative.ScreenToClient(parent, ref position))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        // Child positions are parent-client coordinates. Painting below deliberately
        // passes no destination point to UpdateLayeredWindow, which uses screen coordinates.
        if (!WidgetNative.SetWindowPos(_window.Handle, parent != IntPtr.Zero ? IntPtr.Zero : new IntPtr(-1),
                position.X, position.Y, bounds.Width, bounds.Height, 0x0010u | (show ? 0x0040u : 0)))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static IntPtr FindNotificationWindow(IntPtr taskbar)
    {
        IntPtr direct = WidgetNative.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (direct != IntPtr.Zero) return direct;
        IntPtr found = IntPtr.Zero;
        WidgetNative.EnumChildWindows(taskbar, (window, _) =>
        {
            if (WindowClass(window) != "TrayNotifyWnd") return true;
            found = window;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static List<Rectangle> FindOtherWidgets(IntPtr taskbar, Rectangle taskbarBounds)
    {
        var result = new List<Rectangle>();
        WidgetNative.GetWindowThreadProcessId(taskbar, out uint explorerPid);
        uint ownPid = (uint)Environment.ProcessId;
        IntPtr child = WidgetNative.GetWindow(taskbar, 5); // GW_CHILD: direct children only.
        for (int count = 0; child != IntPtr.Zero && count < 256; count++)
        {
            if (WidgetNative.IsWindowVisible(child) && WidgetNative.GetWindowRect(child, out var native))
            {
                WidgetNative.GetWindowThreadProcessId(child, out uint pid);
                string className = WindowClass(child);
                bool otherApp = pid != explorerPid && pid != ownPid;
                bool weather = className is "ShellFeedsTaskbarViewWindow" or "DynamicContent2";
                Rectangle rectangle = Rectangle.Intersect(native.ToRectangle(), taskbarBounds);
                if ((otherApp || weather) && rectangle.Width > 0 && rectangle.Height > 0)
                    result.Add(rectangle);
            }
            child = WidgetNative.GetWindow(child, 2); // GW_HWNDNEXT.
        }
        return result;
    }

    private static string WindowClass(IntPtr window)
    {
        var name = new StringBuilder(128);
        WidgetNative.GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    private static bool ForegroundCoversScreen(Rectangle screen)
    {
        IntPtr foreground = WidgetNative.GetForegroundWindow();
        string name = WindowClass(foreground);
        if (name is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        if (!WidgetNative.GetWindowRect(foreground, out var native)) return false;
        WidgetNative.GetWindowThreadProcessId(foreground, out uint pid);
        return pid != (uint)Environment.ProcessId && native.ToRectangle().Contains(screen);
    }

    private void RenderAndUpdateTooltip(bool throwOnFailure = false)
    {
        if (_disposed || _window is null || !WidgetNative.IsWindow(_window.Handle)) return;
        try
        {
            if (_needsRender && _bounds.Width > 0 && _bounds.Height > 0) Render();
            UpdateTooltip();
        }
        catch (Exception ex) when (ex is Win32Exception or ExternalException or ArgumentException)
        {
            _needsRender = true;
            if (throwOnFailure) throw;
            LogPlacementFailure(ex);
        }
    }

    /// <summary>
    /// Draw at the taskbar's physical pixel dimensions and hand Windows an actual
    /// premultiplied-alpha DIB. GetHbitmap can discard or alter the alpha channel
    /// of a managed Bitmap, producing hard halos around tiny antialiased glyphs.
    /// </summary>
    private void Render()
    {
        using var bitmap = new Bitmap(_bounds.Width, _bounds.Height, PixelFormat.Format32bppPArgb);
        Color foreground = SystemInformation.HighContrast
            ? SystemColors.WindowText
            : (_floating || _dark ? Color.FromArgb(245, 245, 245) : Color.FromArgb(28, 29, 31));
        Color secondary = SystemInformation.HighContrast ? foreground
            : (_floating || _dark ? Color.FromArgb(221, 221, 221) : Color.FromArgb(62, 62, 62));
        if (!_snapshot.HasOutput) foreground = secondary;

        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // Battery-like indicators have no visible idle capsule. Show the
            // rounded hit area only when hovered, as Explorer's controls do.
            float insetX = S(1);
            float insetY = _vertical ? S(1) : S(2);
            var background = new RectangleF(insetX, insetY,
                bitmap.Width - 2 * insetX, bitmap.Height - 2 * insetY);
            using (var shape = Theme.RoundedRectangle(background, S(7)))
            {
                Color fill = SystemInformation.HighContrast
                    ? SystemColors.Window
                    : _floating
                        ? (_hovered ? Theme.Hover : Theme.Background)
                        : _dark
                            ? Color.FromArgb(_hovered ? 40 : 2, 255, 255, 255)
                            : Color.FromArgb(_hovered ? 30 : 2, 8, 12, 20);
                using var brush = new SolidBrush(fill);
                graphics.FillPath(brush, shape);
                if (_floating)
                {
                    using var border = new Pen(Theme.Border, Math.Max(1, S(1)));
                    graphics.DrawPath(border, shape);
                }
            }

            // A single native-resolution 24px speaker, aligned with the battery
            // glyphs. All text and vectors are rasterized at the taskbar's DPI.
            int iconSize = S(24);
            var iconBounds = _vertical
                ? new RectangleF((bitmap.Width - iconSize) / 2f, S(5), iconSize, iconSize)
                : new RectangleF(_showPercentage ? S(7) : (bitmap.Width - iconSize) / 2f,
                    (bitmap.Height - iconSize) / 2f, iconSize, iconSize);
            Theme.DrawFluentSpeaker(graphics, iconBounds, foreground,
                _snapshot.MasterMuted || !_snapshot.HasOutput, Percent(_snapshot.MasterVolume));

        }

        // Text is composited after disposing Graphics.FromImage: locking the
        // premultiplied bitmap while GDI+ still owns its Graphics can fail with
        // an ObjectBusy error on Windows.
        if (_showPercentage)
        {
            string value = !_snapshot.HasOutput ? "Unavailable" : _snapshot.MasterMuted
                ? "Muted" : $"{Percent(_snapshot.MasterVolume)}%";
            using var nameFont = new Font("Segoe UI", S(13), FontStyle.Regular, GraphicsUnit.Pixel);
            using var valueFont = new Font("Segoe UI", S(13), FontStyle.Regular, GraphicsUnit.Pixel);
            int x = _vertical ? S(2) : S(38);
            int width = _vertical ? bitmap.Width - S(4) : bitmap.Width - x - S(7);
            int top = _vertical ? S(32) : (bitmap.Height - S(30)) / 2;
            LayeredTextRenderer.Draw(bitmap, _displayLabel, nameFont,
                new Rectangle(x, top, width, S(16)), foreground);
            LayeredTextRenderer.Draw(bitmap, value, valueFont,
                new Rectangle(x, top + S(14), width, S(16)), secondary);
        }

        // UpdateLayeredWindow expects premultiplied BGRA pixels. A top-down 32-bit
        // DIB preserves alpha exactly and avoids GetHbitmap's conversion artefacts.
        IntPtr memoryDc = WidgetNative.CreateCompatibleDC(IntPtr.Zero);
        if (memoryDc == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        IntPtr nativeBitmap = IntPtr.Zero;
        IntPtr previous = IntPtr.Zero;
        try
        {
            var info = new WidgetNative.BitmapInfo
            {
                Header = new WidgetNative.BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<WidgetNative.BitmapInfoHeader>(),
                    Width = bitmap.Width,
                    Height = -bitmap.Height, // top-down: row zero is the visual top
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0 // BI_RGB
                }
            };
            nativeBitmap = WidgetNative.CreateDIBSection(memoryDc, ref info, 0,
                out IntPtr pixels, IntPtr.Zero, 0);
            if (nativeBitmap == IntPtr.Zero || pixels == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var sourceRect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData source = bitmap.LockBits(sourceRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                int rowBytes = checked(bitmap.Width * 4);
                var buffer = new byte[rowBytes];
                for (int y = 0; y < bitmap.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(source.Scan0, y * source.Stride), buffer, 0, rowBytes);
                    Marshal.Copy(buffer, 0, IntPtr.Add(pixels, y * rowBytes), rowBytes);
                }
            }
            finally { bitmap.UnlockBits(source); }

            previous = WidgetNative.SelectObject(memoryDc, nativeBitmap);
            if (previous == IntPtr.Zero || previous == new IntPtr(-1))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var size = new WidgetNative.NativeSize(bitmap.Width, bitmap.Height);
            var sourcePoint = new WidgetNative.NativePoint(0, 0);
            var blend = new WidgetNative.BlendFunction { SourceConstantAlpha = 255, AlphaFormat = 1 };
            if (!WidgetNative.UpdateLayeredWindow(_window!.Handle, IntPtr.Zero, IntPtr.Zero,
                    ref size, memoryDc, ref sourcePoint, 0, ref blend, 2))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            _needsRender = false;
        }
        finally
        {
            if (previous != IntPtr.Zero && previous != new IntPtr(-1))
                WidgetNative.SelectObject(memoryDc, previous);
            if (nativeBitmap != IntPtr.Zero) WidgetNative.DeleteObject(nativeBitmap);
            WidgetNative.DeleteDC(memoryDc);
        }
    }

    private void CreateTooltip()
    {
        if (_window is null) return;
        var controls = new WidgetNative.CommonControls { Size = 8, Classes = 0x000000FF };
        WidgetNative.InitCommonControlsEx(ref controls);
        _tooltip = WidgetNative.CreateWindowEx(0x00000008, "tooltips_class32", null,
            unchecked((int)0x80000000) | 0x01 | 0x02, 0, 0, 0, 0, _window.Handle,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_tooltip == IntPtr.Zero) return;
        _tooltipText = Marshal.StringToHGlobalUni("SoundDock");
        var tool = TooltipInfo();
        WidgetNative.SendToolMessage(_tooltip, 0x0432, IntPtr.Zero, ref tool); // TTM_ADDTOOLW.
        WidgetNative.SendMessage(_tooltip, 0x0418, IntPtr.Zero, new IntPtr(S(360))); // TTM_SETMAXTIPWIDTH.
    }

    private WidgetNative.ToolInfo TooltipInfo() => new()
    {
        Size = (uint)Marshal.SizeOf<WidgetNative.ToolInfo>(),
        Flags = 0x0001 | 0x0010, // TTF_IDISHWND | TTF_SUBCLASS.
        Window = _window?.Handle ?? IntPtr.Zero,
        Id = _window?.Handle ?? IntPtr.Zero,
        Text = _tooltipText
    };

    private void UpdateTooltip()
    {
        if (_window is null) return;
        string text = _snapshot.HasOutput
            ? $"{_snapshot.DefaultDeviceName}\n{(_snapshot.MasterMuted ? "Muted · " : "")}{Percent(_snapshot.MasterVolume)}% volume\nClick to switch output · Scroll to change volume"
            : "SoundDock\nNo connected sound output\nClick to open audio controls";
        if (text == _lastTooltip) return;
        WidgetNative.SetWindowText(_window.Handle, "SoundDock — " + (_snapshot.DefaultDeviceName ?? "No output") + " — " + Percent(_snapshot.MasterVolume) + "%");
        if (_tooltip != IntPtr.Zero && WidgetNative.IsWindow(_tooltip))
        {
            IntPtr previous = _tooltipText;
            _tooltipText = Marshal.StringToHGlobalUni(text);
            var tool = TooltipInfo();
            WidgetNative.SendToolMessage(_tooltip, 0x0439, IntPtr.Zero, ref tool); // TTM_UPDATETIPTEXTW.
            if (previous != IntPtr.Zero) Marshal.FreeHGlobal(previous);
        }
        _lastTooltip = text;
    }

    // The painted corners are transparent; don't keep a rectangular mouse hitbox.
    // WM_NCHITTEST uses screen coordinates, while this test uses widget pixels.
    private bool HitsRoundedSurface(int x, int y)
    {
        float left = S(1);
        float top = _vertical ? S(1) : S(2);
        float right = _bounds.Width - left;
        float bottom = _bounds.Height - top;
        if (x < left || x >= right || y < top || y >= bottom) return false;
        float radius = Math.Min(S(7), Math.Min((right - left) / 2, (bottom - top) / 2));
        float nearestX = Math.Clamp(x, left + radius, right - radius);
        float nearestY = Math.Clamp(y, top + radius, bottom - radius);
        float dx = x - nearestX;
        float dy = y - nearestY;
        return dx * dx + dy * dy <= radius * radius;
    }

    private void Hover(bool hovered)
    {
        if (_hovered == hovered) return;
        _hovered = hovered;
        _needsRender = true;
        RenderAndUpdateTooltip();
        if (hovered && _window is not null)
        {
            var tracking = new WidgetNative.MouseTracking
            {
                Size = (uint)Marshal.SizeOf<WidgetNative.MouseTracking>(), Flags = 0x00000002, Window = _window.Handle
            };
            WidgetNative.TrackMouseEvent(ref tracking);
        }
    }

    private void Wheel(IntPtr wParam)
    {
        _wheelRemainder += unchecked((short)((long)wParam >> 16));
        int steps = _wheelRemainder / 120;
        _wheelRemainder %= 120;
        if (steps != 0) VolumeStepRequested?.Invoke(steps * 2);
    }

    private void LogPlacementFailure(Exception ex)
    {
        if (DateTime.UtcNow - _lastFailureLog < TimeSpan.FromSeconds(30)) return;
        _lastFailureLog = DateTime.UtcNow;
        AppDiagnostics.Write(ex);
    }

    private void DestroyWindow()
    {
        if (_tooltip != IntPtr.Zero && WidgetNative.IsWindow(_tooltip)) WidgetNative.DestroyWindow(_tooltip);
        _tooltip = IntPtr.Zero;
        if (_tooltipText != IntPtr.Zero) Marshal.FreeHGlobal(_tooltipText);
        _tooltipText = IntPtr.Zero;
        _window?.DestroyHandle();
        _window = null;
        _parent = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DestroyWindow();
    }

    private sealed class WidgetWindow(TaskbarWidget owner) : NativeWindow
    {
        protected override void WndProc(ref Message message)
        {
            if (!owner._disposed)
            {
                switch (message.Msg)
                {
                    case 0x0021: // WM_MOUSEACTIVATE: opening a control should not focus the taskbar child.
                        message.Result = new IntPtr(3); return; // MA_NOACTIVATE.
                    case 0x0084: // WM_NCHITTEST.
                        if (WidgetNative.GetCursorPos(out var cursor) &&
                            WidgetNative.GetWindowRect(Handle, out var windowRect) &&
                            !owner.HitsRoundedSurface(cursor.X - windowRect.Left, cursor.Y - windowRect.Top))
                        {
                            message.Result = new IntPtr(-1); return; // HTTRANSPARENT.
                        }
                        message.Result = new IntPtr(1); return; // HTCLIENT.
                    case 0x0020: // WM_SETCURSOR.
                        WidgetNative.SetCursor(Cursors.Hand.Handle);
                        message.Result = new IntPtr(1); return;
                    case 0x0200: owner.Hover(true); break; // WM_MOUSEMOVE.
                    case 0x02A3: owner.Hover(false); break; // WM_MOUSELEAVE.
                    case 0x0202: // WM_LBUTTONUP.
                        using (WidgetDpiScope.PerMonitor()) owner.OpenRequested?.Invoke(owner.PopupAnchor);
                        return;
                    case 0x0208: // WM_MBUTTONUP.
                        using (WidgetDpiScope.PerMonitor()) owner.ToggleMuteRequested?.Invoke();
                        return;
                    case 0x0205: // WM_RBUTTONUP.
                    case 0x007B: // WM_CONTEXTMENU.
                        using (WidgetDpiScope.PerMonitor())
                        {
                            WidgetNative.GetCursorPos(out var point);
                            owner.MenuRequested?.Invoke(new Point(point.X, point.Y));
                        }
                        return;
                    case 0x020A: // WM_MOUSEWHEEL.
                        using (WidgetDpiScope.PerMonitor()) owner.Wheel(message.WParam);
                        return;
                    case 0x0014: // WM_ERASEBKGND: per-pixel drawing owns the entire image.
                        message.Result = new IntPtr(1); return;
                    case 0x000F: // WM_PAINT: the compositor retains the bitmap.
                        WidgetNative.ValidateRect(Handle, IntPtr.Zero);
                        return;
                    case 0x007E: // WM_DISPLAYCHANGE.
                    case 0x02E0: // WM_DPICHANGED.
                    case 0x031A: // WM_THEMECHANGED.
                        owner._needsRender = true;
                        break;
                }
            }
            // WM_DESTROY/WM_NCDESTROY only detach this HWND. The application and its
            // hidden top-level receiver remain alive to recreate it after Explorer.
            base.WndProc(ref message);
        }
    }
}

internal readonly struct WidgetDpiScope : IDisposable
{
    private readonly IntPtr _previous;
    private WidgetDpiScope(IntPtr context) => _previous = context == IntPtr.Zero ? IntPtr.Zero : WidgetNative.SetThreadDpiAwarenessContext(context);
    internal static WidgetDpiScope PerMonitor() => new(new IntPtr(-4));
    internal static WidgetDpiScope ForWindow(IntPtr window) => new(WidgetNative.GetWindowDpiAwarenessContext(window));
    public void Dispose() { if (_previous != IntPtr.Zero) WidgetNative.SetThreadDpiAwarenessContext(_previous); }
}

internal static class WidgetNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct NativePoint(int x, int y) { public int X = x; public int Y = y; }
    [StructLayout(LayoutKind.Sequential)] internal struct NativeSize(int width, int height) { public int Width = width; public int Height = height; }
    [StructLayout(LayoutKind.Sequential)] internal struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] internal struct BlendFunction { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)] internal struct BitmapInfoHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint FirstColor;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseTracking { public uint Size, Flags; public IntPtr Window; public uint HoverTime; }
    [StructLayout(LayoutKind.Sequential)] internal struct CommonControls { public uint Size, Classes; }
    [StructLayout(LayoutKind.Sequential)] internal struct ToolInfo
    {
        public uint Size, Flags;
        public IntPtr Window, Id;
        public NativeRect Rectangle;
        public IntPtr Instance, Text, Parameter, Reserved;
    }
    internal delegate bool WindowEnumerator(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string className, string? caption);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string? caption);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumChildWindows(IntPtr parent, WindowEnumerator callback, IntPtr parameter);
    [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr window, StringBuilder name, int size);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ScreenToClient(IntPtr window, ref NativePoint point);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")] internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] internal static extern IntPtr SetCursor(IntPtr cursor);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool TrackMouseEvent(ref MouseTracking tracking);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ValidateRect(IntPtr window, IntPtr rectangle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowText(IntPtr window, string text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern IntPtr CreateWindowEx(int extendedStyle, string className, string? title, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] internal static extern IntPtr SendToolMessage(IntPtr window, uint message, IntPtr wParam, ref ToolInfo tool);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool InitCommonControlsEx(ref CommonControls controls);
    [DllImport("gdi32.dll", SetLastError = true)] internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] internal static extern IntPtr CreateDIBSection(
        IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr pixels, IntPtr section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)] internal static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDc, IntPtr destination, ref NativeSize size, IntPtr sourceDc, ref NativePoint source, uint colorKey, ref BlendFunction blend, uint flags);
}
