using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RdpManager.Presentation.Services;

namespace RdpManager.Presentation.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Deskpin v{v?.ToString(3) ?? "1.0.0"}";
    }

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        try
        {
            await App.Host.Services.GetRequiredService<UpdateChecker>().CheckAsync();
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }
}
