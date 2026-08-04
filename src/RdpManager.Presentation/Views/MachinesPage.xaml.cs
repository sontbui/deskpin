using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using RdpManager.Presentation.ViewModels;

namespace RdpManager.Presentation.Views;

public sealed partial class MachinesPage : Page
{
    public MachinesViewModel Vm { get; }

    private bool _dialogOpen;

    public MachinesPage()
    {
        Vm = App.Host.Services.GetRequiredService<MachinesViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // The VM is a singleton; each page instance must subscribe on load and UNSUBSCRIBE on unload,
    // otherwise navigating away and back stacks handlers and fires multiple dialogs at once
    // ("Only a single ContentDialog can be open").
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Vm.ConfigureDisplayRequested -= OnConfigureDisplayRequested;
        Vm.ConfigureDisplayRequested += OnConfigureDisplayRequested;
        await Vm.LoadCommand.ExecuteAsync(CancellationToken.None);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
        => Vm.ConfigureDisplayRequested -= OnConfigureDisplayRequested;

    // Selecting the row before its menu opens, so the ⋯ actions operate on the right machine.
    private void OnItemMenuClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is MachineItemViewModel m)
            Vm.Selected = m;
    }

    private async void OnMachineDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is MachineItemViewModel m)
            Vm.Selected = m;
        if (Vm.Selected is not null)
            await Vm.RemoteCommand.ExecuteAsync(null);
    }

    private async void OnRemoteItem(object sender, RoutedEventArgs e) => await Vm.RemoteCommand.ExecuteAsync(null);
    private async void OnPingItem(object sender, RoutedEventArgs e) => await Vm.PingCommand.ExecuteAsync(null);
    private async void OnDeleteItem(object sender, RoutedEventArgs e) => await Vm.DeleteCommand.ExecuteAsync(null);
    private async void OnConfigureItem(object sender, RoutedEventArgs e) => await Vm.ConfigureDisplayAsync(CancellationToken.None);

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AddMachineDialog { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.Result is not null)
            await Vm.AddMachineAsync(dialog.Result, dialog.Password, CancellationToken.None);
    }

    private async void OnEditItem(object sender, RoutedEventArgs e)
    {
        if (Vm.Selected is null) return;
        var machineId = Vm.Selected.Id;
        var dialog = new AddMachineDialog { XamlRoot = XamlRoot };
        dialog.Prefill(Vm.Selected.Model);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.Result is not null)
            await Vm.EditMachineAsync(machineId, dialog.Result, dialog.Password, CancellationToken.None);
    }

    // View mechanics only: host the picker VM in a dialog. The decision to open came from the VM.
    private async void OnConfigureDisplayRequested(MonitorPickerViewModel picker)
    {
        if (_dialogOpen) return; // never open a second ContentDialog on top of another
        _dialogOpen = true;
        try
        {
            var dialog = new MonitorPickerDialog(picker) { XamlRoot = XamlRoot };
            picker.Saved += () => dialog.Hide();
            await dialog.ShowAsync();
        }
        finally
        {
            _dialogOpen = false;
        }
    }
}
