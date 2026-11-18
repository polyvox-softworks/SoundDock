namespace SoundDock.UI;

/// <summary>A small standard Windows dialog for a custom taskbar device label.</summary>
internal static class OutputLabelDialog
{
    internal static string? Show(string deviceName, string currentLabel)
    {
        using var dialog = new Form
        {
            Text = "Taskbar label",
            AccessibleName = "Rename active sound output",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            ShowInTaskbar = false,
            MaximizeBox = false,
            MinimizeBox = false,
            TopMost = true,
            Font = new Font("Segoe UI", 9f),
            ClientSize = new Size(350, 161)
        };
        var device = new Label
        {
            Text = deviceName,
            AutoEllipsis = true,
            UseMnemonic = false,
            Location = new Point(16, 13),
            Size = new Size(318, 24)
        };
        var hint = new Label
        {
            Text = "Label shown beside the taskbar speaker:",
            Location = new Point(16, 42),
            Size = new Size(318, 22)
        };
        var text = new TextBox
        {
            Text = currentLabel,
            MaxLength = OutputLabel.MaxLength,
            Location = new Point(16, 66),
            Size = new Size(318, 25),
            AccessibleName = "Taskbar output label"
        };
        var resetHint = new Label
        {
            Text = "Leave empty to use the automatic name.",
            ForeColor = SystemColors.GrayText,
            Font = new Font("Segoe UI", 8f),
            Location = new Point(16, 94),
            Size = new Size(318, 18)
        };
        var ok = new Button
        {
            Text = "Save", DialogResult = DialogResult.OK,
            Location = new Point(168, 124), Size = new Size(79, 26)
        };
        var cancel = new Button
        {
            Text = "Cancel", DialogResult = DialogResult.Cancel,
            Location = new Point(255, 124), Size = new Size(79, 26)
        };
        dialog.Controls.AddRange([device, hint, text, resetHint, ok, cancel]);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        dialog.Shown += (_, _) => { text.Focus(); text.SelectAll(); };
        var result = dialog.ShowDialog();
        resetHint.Font.Dispose();
        return result == DialogResult.OK ? text.Text.Trim() : null;
    }
}
