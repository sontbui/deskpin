using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using RdpManager.Application.Files;
using RdpManager.Presentation.Services;
using RdpManager.Presentation.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace RdpManager.Presentation.Views;

/// <summary>
/// The merged Machines &amp; Files destination. Code-behind stays thin: it forwards selection,
/// drag/drop and menu clicks to the ViewModels, and hosts the dialogs (add/edit machine,
/// monitor picker, drive redirection). All decisions live in the ViewModels.
/// </summary>
public sealed partial class MachinesFilesPage : Page
{
    public MachinesFilesViewModel Vm { get; }

    private readonly IToastService _toasts;
    private bool _pickerOpen;

    public MachinesFilesPage()
    {
        Vm = App.Host.Services.GetRequiredService<MachinesFilesViewModel>();
        _toasts = App.Host.Services.GetRequiredService<IToastService>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // The VMs are singletons; subscribe on load and UNSUBSCRIBE on unload so navigating away
    // and back never stacks handlers ("Only a single ContentDialog can be open").
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Vm.Master.ConfigureDisplayRequested -= OnConfigureDisplayRequested;
        Vm.Master.ConfigureDisplayRequested += OnConfigureDisplayRequested;
        try
        {
            await Vm.LoadCommand.ExecuteAsync(CancellationToken.None);
        }
        catch (System.Exception ex)
        {
            _toasts.Show($"Couldn't load Machines & Files: {ex.Message}");
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
        => Vm.Master.ConfigureDisplayRequested -= OnConfigureDisplayRequested;

    // ── Machine master ──────────────────────────────────────────────────────

    private async void OnMachineDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is MachineItemViewModel m)
            Vm.Master.Selected = m;
        if (Vm.Master.Selected is not null)
            await Vm.Master.RemoteCommand.ExecuteAsync(null);
    }

    private void SelectRowUnderMenu(object sender)
    {
        if ((sender as FrameworkElement)?.DataContext is MachineItemViewModel m)
            Vm.Master.Selected = m;
    }

    private async void OnRemoteItem(object sender, RoutedEventArgs e)
    { SelectRowUnderMenu(sender); await Vm.Master.RemoteCommand.ExecuteAsync(null); }

    private async void OnPingItem(object sender, RoutedEventArgs e)
    { SelectRowUnderMenu(sender); await Vm.Master.PingCommand.ExecuteAsync(null); }

    private async void OnDeleteItem(object sender, RoutedEventArgs e)
    { SelectRowUnderMenu(sender); await Vm.Master.DeleteCommand.ExecuteAsync(null); }

    private async void OnConfigureItem(object sender, RoutedEventArgs e)
    { SelectRowUnderMenu(sender); await Vm.Master.ConfigureDisplayAsync(CancellationToken.None); }

    private async void OnEditItem(object sender, RoutedEventArgs e)
    { SelectRowUnderMenu(sender); await EditSelectedAsync(); }

    // ── Header toolbar ──────────────────────────────────────────────────────

    private async void OnPingClick(object sender, RoutedEventArgs e) =>
        await Vm.Master.PingCommand.ExecuteAsync(null);

    private async void OnRemoteClick(object sender, RoutedEventArgs e) =>
        await Vm.Master.RemoteCommand.ExecuteAsync(null);

    private async void OnDisplayClick(object sender, RoutedEventArgs e) =>
        await Vm.Master.ConfigureDisplayAsync(CancellationToken.None);

    private async void OnEditClick(object sender, RoutedEventArgs e) => await EditSelectedAsync();

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AddMachineDialog { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.Result is not null)
        {
            await Vm.Master.AddMachineAsync(dialog.Result, dialog.Password, CancellationToken.None);
            _toasts.Show($"Added “{dialog.Result.Name}”.");
            _ = Vm.ProbeAllAsync();
            await OfferFileAccessAsync(dialog.Result);
        }
    }

    private async void OnEnableFileAccessItem(object sender, RoutedEventArgs e)
    {
        SelectRowUnderMenu(sender);
        await RunEnableFileAccessAsync();
    }

    // After adding/editing a Windows machine, offer the one-time admin-share setup.
    private async System.Threading.Tasks.Task OfferFileAccessAsync(RdpManager.Application.Machines.CreateMachineRequest request)
    {
        if (request.Os != RdpManager.Domain.Enums.MachineOs.Windows) return;

        var confirm = new ContentDialog
        {
            Title = $"Enable file browsing on {request.Name}?",
            Content = "Deskpin can turn on the admin-share access policy on this machine so its drives " +
                      "are browsable here. It needs the saved admin password and Remote Registry reachable; " +
                      "if the remote is a workgroup local admin you'll get a one-line command to run there instead.",
            PrimaryButtonText = "Enable now",
            CloseButtonText = "Later",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
            await RunEnableFileAccessAsync();
    }

    private async System.Threading.Tasks.Task RunEnableFileAccessAsync()
    {
        var result = await Vm.EnableFileAccessAsync(CancellationToken.None);
        if (result is null) return;

        if (result.Success)
        {
            _toasts.Show(result.Message);
            return;
        }

        // Couldn't do it remotely: copy the fix command and show the manual step.
        if (!string.IsNullOrEmpty(result.Command))
        {
            var data = new DataPackage();
            data.SetText(result.Command);
            Clipboard.SetContent(data);
        }
        await new ContentDialog
        {
            Title = "One manual step needed",
            Content = new TextBlock
            {
                Text = result.Message + "\n\n(The command has been copied to your clipboard.)",
                TextWrapping = TextWrapping.Wrap,
            },
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
        }.ShowAsync();
    }

    private async System.Threading.Tasks.Task EditSelectedAsync()
    {
        if (Vm.Master.Selected is null) return;
        var machineId = Vm.Master.Selected.Id;
        var dialog = new AddMachineDialog { XamlRoot = XamlRoot };
        dialog.Prefill(Vm.Master.Selected.Model);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.Result is not null)
        {
            await Vm.Master.EditMachineAsync(machineId, dialog.Result, dialog.Password, CancellationToken.None);
            _toasts.Show($"Saved changes to “{dialog.Result.Name}”.");
            _ = Vm.ProbeAllAsync();
        }
    }

    // View mechanics only: host the picker VM in a dialog. The decision to open came from the VM.
    private async void OnConfigureDisplayRequested(MonitorPickerViewModel picker)
    {
        if (_pickerOpen) return;
        _pickerOpen = true;
        try
        {
            var dialog = new MonitorPickerDialog(picker) { XamlRoot = XamlRoot };
            picker.Saved += () => dialog.Hide();
            await dialog.ShowAsync();
            _toasts.Show("Display layout saved.");
        }
        finally
        {
            _pickerOpen = false;
        }
    }

    // ── Pane selection / open ───────────────────────────────────────────────

    private void OnLocalSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        Vm.Console.LocalPane.SetSelection(LocalList.SelectedItems.OfType<FileEntryViewModel>());

    private void OnRemoteSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        Vm.Console.RemotePane.SetSelection(RemoteList.SelectedItems.OfType<FileEntryViewModel>());

    private async void OnLocalDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is FileEntryViewModel entry)
            await Vm.Console.LocalPane.OpenAsync(entry);
    }

    private async void OnRemoteDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is FileEntryViewModel entry)
            await Vm.Console.RemotePane.OpenAsync(entry);
    }

    // ── Breadcrumbs & address bar ───────────────────────────────────────────

    private async void OnLocalCrumbClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BreadcrumbItemViewModel crumb)
            await Vm.Console.LocalPane.NavigateAsync(crumb.Path);
    }

    private async void OnRemoteCrumbClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BreadcrumbItemViewModel crumb)
            await Vm.Console.RemotePane.NavigateAsync(crumb.Path);
    }

    private async void OnLocalPathKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) await Vm.Console.LocalPane.GoToPathAsync();
        else if (e.Key == Windows.System.VirtualKey.Escape) Vm.Console.LocalPane.CancelEditPath();
    }

    private async void OnRemotePathKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) await Vm.Console.RemotePane.GoToPathAsync();
        else if (e.Key == Windows.System.VirtualKey.Escape) Vm.Console.RemotePane.CancelEditPath();
    }

    // ── Drag & drop across the divider ──────────────────────────────────────

    private void OnLocalDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        e.Data.RequestedOperation = DataPackageOperation.Copy;
        Vm.Console.BeginDrag(TransferEndpoint.Local, e.Items.OfType<FileEntryViewModel>());
    }

    private void OnRemoteDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        e.Data.RequestedOperation = DataPackageOperation.Copy;
        Vm.Console.BeginDrag(TransferEndpoint.Remote, e.Items.OfType<FileEntryViewModel>());
    }

    private void OnPaneDragOver(object sender, DragEventArgs e) =>
        e.AcceptedOperation = DataPackageOperation.Copy;

    private async void OnLocalPaneDrop(object sender, DragEventArgs e) =>
        await Vm.Console.DropOnAsync(TransferEndpoint.Local);

    private async void OnRemotePaneDrop(object sender, DragEventArgs e) =>
        await Vm.Console.DropOnAsync(TransferEndpoint.Remote);

    // ── Row context menus ───────────────────────────────────────────────────

    private static FileEntryViewModel? EntryOf(object sender) =>
        (sender as FrameworkElement)?.DataContext as FileEntryViewModel;

    private async void OnLocalUploadItem(object sender, RoutedEventArgs e)
    {
        if (EntryOf(sender) is { } entry)
            await Vm.Console.TransferAsync(Domain.Enums.TransferDirection.Upload, new[] { entry });
    }

    private async void OnLocalRenameItem(object sender, RoutedEventArgs e)
    {
        if (EntryOf(sender) is { } entry) await Vm.Console.RenameAsync(Vm.Console.LocalPane, entry);
    }

    private async void OnLocalNewFolderItem(object sender, RoutedEventArgs e) =>
        await Vm.Console.NewFolderAsync(Vm.Console.LocalPane);

    private async void OnLocalPropertiesItem(object sender, RoutedEventArgs e)
    {
        if (EntryOf(sender) is { } entry) await Vm.Console.ShowPropertiesAsync(entry);
    }

    private async void OnLocalDeleteItem(object sender, RoutedEventArgs e)
    {
        if (EntryOf(sender) is { } entry) await Vm.Console.DeleteAsync(Vm.Console.LocalPane, entry);
    }

    private async void OnRemoteDownloadItem(object sender, RoutedEventArgs e)
    {
        if (EntryOf(sender) is { } entry)
            await Vm.Console.TransferAsync(Domain.Enums.TransferDirection.Download, new[] { entry });
    }

    private async void OnRemoteRenameItem(object sender, RoutedEventArgs e)
    {
        if (EntryOf(sender) is { } entry) await Vm.Console.RenameAsync(Vm.Console.RemotePane, entry);
    }

    private async void OnRemoteNewFolderItem(object sender, RoutedEventArgs e) =>
        await Vm.Console.NewFolderAsync(Vm.Console.RemotePane);

    private async void OnRemotePropertiesItem(object sender, RoutedEventArgs e)
    {
        if (EntryOf(sender) is { } entry) await Vm.Console.ShowPropertiesAsync(entry);
    }

    private async void OnRemoteDeleteItem(object sender, RoutedEventArgs e)
    {
        if (EntryOf(sender) is { } entry) await Vm.Console.DeleteAsync(Vm.Console.RemotePane, entry);
    }

    // ── Drive redirection dialog ────────────────────────────────────────────

    private async void OnOpenRedirection(object sender, RoutedEventArgs e)
    {
        if (Vm.Master.Selected is null) return;
        var current = await Vm.Console.GetRedirectionAsync();
        var roots = await Vm.Console.GetLocalRootsAsync();
        var dialog = new DriveRedirectionDialog(current, roots) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await Vm.Console.ApplyRedirectionAsync(dialog.Result);
    }
}
