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
