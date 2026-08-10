using System;

namespace RdpManager.Presentation.ViewModels;

/// <summary>Human formatting for the Files console. Pure and boring on purpose.</summary>
internal static class TransferFormat
{
    public static string Bytes(long bytes)
    {
        if (bytes < 0) return "—";
        double value = bytes;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{bytes} B" : value < 10 ? $"{value:0.#} {units[unit]}" : $"{value:0} {units[unit]}";
    }

    public static string Speed(double bytesPerSecond) => $"{Bytes((long)bytesPerSecond)}/s";

    public static string Eta(TimeSpan remaining)
    {
        if (remaining.TotalSeconds < 1) return "<1s left";
        if (remaining.TotalMinutes < 1) return $"{(int)remaining.TotalSeconds}s left";
        if (remaining.TotalHours < 1) return $"{(int)remaining.TotalMinutes}m {remaining.Seconds}s left";
        return $"{(int)remaining.TotalHours}h {remaining.Minutes}m left";
    }

    public static string When(DateTimeOffset moment)
    {
        var local = moment.ToLocalTime();
        var today = DateTimeOffset.Now.Date;
        if (local.Date == today) return local.ToString("HH:mm");
        if (local.Date == today.AddDays(-1)) return "Yesterday";
        return local.ToString("MMM d");
    }

    public static string Modified(DateTimeOffset? moment) => moment is null ? "—" : When(moment.Value);
}
