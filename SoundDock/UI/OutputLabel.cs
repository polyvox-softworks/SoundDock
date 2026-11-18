using SoundDock.Audio;

namespace SoundDock.UI;

/// <summary>
/// Human-readable labels for the tiny taskbar indicator. Preserve the original
/// endpoint name in Windows and in the output picker; shortening only affects
/// the indicator, and the full name remains available in its tooltip.
/// </summary>
internal static class OutputLabel
{
    internal const int MaxLength = 20;

    internal static string For(AudioSnapshot snapshot, IReadOnlyDictionary<string, string> overrides)
    {
        if (!snapshot.HasOutput) return "No output";
        if (snapshot.DefaultDeviceId is { } id && overrides.TryGetValue(id, out var alias) &&
            !string.IsNullOrWhiteSpace(alias))
            return Shorten(alias.Trim());

        string name = snapshot.DefaultDeviceName ?? "Speakers";
        string label = BaseName(name);
        if (snapshot.Devices.Count(device =>
            string.Equals(BaseName(device.Name), label, StringComparison.OrdinalIgnoreCase)) > 1)
        {
            // Two endpoints may both be called "Speakers". Add the beginning of
            // the differentiating manufacturer/model name when available.
            int open = name.IndexOf('(');
            if (open > 0)
            {
                string suffix = name[(open + 1)..].TrimEnd(')', ' ');
                if (suffix.Length > 0) label += " · " + suffix;
            }
        }
        return Shorten(label);
    }

    internal static string BaseName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Speakers";
        string name = value.Trim();
        int parenthesis = name.IndexOf(" (", StringComparison.Ordinal);
        if (parenthesis > 0) name = name[..parenthesis];
        return name.Trim();
    }

    internal static string Shorten(string text) => text.Length <= MaxLength
        ? text : text[..(MaxLength - 1)].TrimEnd() + "…";
}
