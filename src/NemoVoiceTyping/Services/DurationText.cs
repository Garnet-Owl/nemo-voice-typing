using System;
using System.Globalization;

namespace NemoVoiceTyping.Services;

/// <summary>
/// Parses and formats user-entered durations for the mic idle-timeout
/// picker. Accepts a bare number (seconds) or a number with an s/m/h
/// suffix, e.g. "45", "90s", "2m", "1h".
/// </summary>
public static class DurationText
{
    public const int MinSeconds = 5;
    public const int MaxSeconds = 5 * 60 * 60;

    public static bool TryParseSeconds(string? text, out int seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var t = text.Trim().ToLowerInvariant();
        var multiplier = 1;
        if (t.EndsWith('h')) { multiplier = 3600; t = t[..^1]; }
        else if (t.EndsWith('m')) { multiplier = 60; t = t[..^1]; }
        else if (t.EndsWith('s')) { t = t[..^1]; }

        if (!double.TryParse(t.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return false;

        var total = (int)Math.Round(value * multiplier);
        if (total < MinSeconds || total > MaxSeconds) return false;

        seconds = total;
        return true;
    }

    public static string Format(int seconds)
    {
        if (seconds % 3600 == 0) return $"{seconds / 3600}h";
        if (seconds % 60 == 0) return $"{seconds / 60}m";
        return $"{seconds}s";
    }
}
