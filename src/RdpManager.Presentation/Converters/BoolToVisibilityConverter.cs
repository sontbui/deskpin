using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace RdpManager.Presentation.Converters;

/// <summary>true → Visible, false → Collapsed. Used for the "active session" dot.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility.Visible;
}

/// <summary>true → Collapsed, false → Visible. Used for "not editing" / "no machines" states.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is not Visibility.Visible;
}

/// <summary>
/// Maps a transfer state string to its Nocturne brush: "Done" → SuccessBrush,
/// "Failed" → ErrorBrush, "Muted" → TextMutedBrush, anything else → AccentBrush.
/// Brushes come from the merged Nocturne dictionary — no colors are defined here.
/// </summary>
public sealed class TransferStateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = (value as string) switch
        {
            "Done" => "SuccessBrush",
            "Failed" => "ErrorBrush",
            "Muted" => "TextMutedBrush",
            _ => "AccentBrush",
        };
        var resources = Microsoft.UI.Xaml.Application.Current.Resources;
        // Lookup consults merged dictionaries; guard anyway so a missing key can never
        // surface as an app-level unhandled exception from inside a binding.
        return resources.TryGetValue(key, out var brush) ? brush : resources["AccentBrush"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
