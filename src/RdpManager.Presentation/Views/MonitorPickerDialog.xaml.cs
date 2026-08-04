using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RdpManager.Presentation.ViewModels;

namespace RdpManager.Presentation.Views;

public sealed partial class MonitorPickerDialog : ContentDialog
{
    public MonitorPickerViewModel Vm { get; }

    public MonitorPickerDialog(MonitorPickerViewModel vm)
    {
        Vm = vm;
        InitializeComponent();

        PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            await Vm.SaveCommand.ExecuteAsync(CancellationToken.None);
            deferral.Complete();
        };

        // "Use all" selects every monitor but keeps the dialog open to pick the primary.
        SecondaryButtonClick += (_, args) =>
        {
            args.Cancel = true;
            Vm.UseAllScreensCommand.Execute(null);
        };
    }

    private void OnSetPrimary(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is MonitorTileViewModel tile)
            Vm.SetSessionPrimary(tile);
    }
}
