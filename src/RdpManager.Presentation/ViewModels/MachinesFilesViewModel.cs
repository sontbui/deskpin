using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RdpManager.Application.Abstractions;
using RdpManager.Presentation.Services;

namespace RdpManager.Presentation.ViewModels;

/// <summary>
/// The merged "Machines &amp; Files" destination: the machine master list on the left drives the
/// file console on the right. Thin composition — <see cref="Master"/> (the existing
/// MachinesViewModel) owns the list, search and Ping/Remote/Display/Delete actions;
/// <see cref="Console"/> (FilesViewModel) owns panes, transfers and drive redirection.
/// This type only glues selection together and enriches rows with live reachability/latency.
/// </summary>
public sealed partial class MachinesFilesViewModel : ObservableObject
{
    private readonly IReachabilityProbe _probe;
    private readonly IDispatcherService _dispatcher;
    private readonly RdpManager.Application.Files.IRemoteAdminSetup _adminSetup;

    public MachinesFilesViewModel(
        MachinesViewModel master, FilesViewModel console,
        IReachabilityProbe probe, IDispatcherService dispatcher,
        RdpManager.Application.Files.IRemoteAdminSetup adminSetup)
    {
        Master = master;
        Console = console;
        _probe = probe;
        _dispatcher = dispatcher;
        _adminSetup = adminSetup;

        Master.PropertyChanged += OnMasterPropertyChanged;
        Master.Machines.CollectionChanged += (_, _) => UpdateHasMachines();
    }

    public MachinesViewModel Master { get; }
    public FilesViewModel Console { get; }

    /// <summary>True when a machine is selected — enables the toolbar actions.</summary>
    [ObservableProperty] private bool _hasSelection;

    /// <summary>Remote needs a selection that actually speaks RDP — macOS does not.</summary>
    [ObservableProperty] private bool _canRemote;

    /// <summary>False only when no machine exists at all (a search with no hits doesn't count).</summary>
    [ObservableProperty] private bool _hasMachines = true;

    private void UpdateHasMachines() =>
        HasMachines = Master.Machines.Count > 0 || !string.IsNullOrWhiteSpace(Master.Search);

    private void OnMasterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MachinesViewModel.Selected)) return;
        Console.SelectedMachine = Master.Selected;
        HasSelection = Master.Selected is not null;
        CanRemote = Master.Selected is { } m && m.Model.Os != Domain.Enums.MachineOs.MacOs;
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct)
    {
        await Console.LoadAsync(ct);   // local pane opens at the user profile
        await Master.LoadAsync(ct);    // machine list (+ auto-selects the first row)
        Console.SelectedMachine = Master.Selected;
        HasSelection = Master.Selected is not null;
        CanRemote = Master.Selected is { } selected && selected.Model.Os != Domain.Enums.MachineOs.MacOs;
        UpdateHasMachines();
        _ = ProbeAllAsync();           // latency column fills in as answers arrive
    }

    /// <summary>
    /// Enables C$ admin-share file access on the selected Windows machine (best-effort remotely).
    /// Returns null when nothing is selected; otherwise the result to surface to the user.
    /// </summary>
    public async Task<RdpManager.Application.Files.AdminSetupResult?> EnableFileAccessAsync(CancellationToken ct)
    {
        if (Master.Selected is null) return null;
        var machine = Master.Selected.Model;
        if (machine.Os != Domain.Enums.MachineOs.Windows)
            return new RdpManager.Application.Files.AdminSetupResult(
                false, false, "File-access setup only applies to Windows machines.", string.Empty);

        var result = await _adminSetup.EnableAdminShareAsync(Master.Selected.Id, machine.Host.Host, ct);
        if (result.Success && !result.AlreadyEnabled)
            await Console.RefreshForSelectedAsync(); // re-auth + reload the remote pane
        return result;
    }

    /// <summary>Probes every listed machine concurrently and paints dot + latency per row.</summary>
    [RelayCommand]
    public async Task ProbeAllAsync()
    {
        var rows = Master.Machines.ToList();
        await Task.WhenAll(rows.Select(async row =>
        {
            try
            {
                var result = await _probe.CheckAsync(row.Model.Host, CancellationToken.None);
                _dispatcher.Post(() =>
                {
                    row.IsUp = result.IsReachable;
                    row.LatencyText = result.IsReachable && result.RoundtripMs is int ms ? $"{ms} ms" : "—";
                });
            }
            catch
            {
                _dispatcher.Post(() => { row.IsUp = false; row.LatencyText = "—"; });
            }
        }));
    }
}
