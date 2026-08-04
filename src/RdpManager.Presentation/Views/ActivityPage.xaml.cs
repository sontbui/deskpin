using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using RdpManager.Presentation.ViewModels;

namespace RdpManager.Presentation.Views;

public sealed partial class ActivityPage : Page
{
    public ActivityViewModel Vm { get; }

    public ActivityPage()
    {
        Vm = App.Host.Services.GetRequiredService<ActivityViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await Vm.LoadCommand.ExecuteAsync(CancellationToken.None);
    }
}
