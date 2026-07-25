using System.Threading;
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
    }
}
