using System.Linq;
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

        var note = App.Host.Services.GetRequiredService<ReleaseNotesReader>().ForCurrentVersion();
        if (note is not null && note.Bullets.Count > 0)
        {
            WhatsNewTitle.Text = $"What's new — {note.Title}";
            WhatsNewText.Text = string.Join("\n", note.Bullets.Select(b => "•  " + b));
        }
        else
        {
            WhatsNewTitle.Visibility = Visibility.Collapsed;
            WhatsNewText.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        try
        {
            await App.Host.Services.GetRequiredService<UpdateChecker>().CheckAsync(announceResult: true);
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }
}
