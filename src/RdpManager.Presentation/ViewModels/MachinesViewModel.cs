using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RdpManager.Application.Abstractions;
using RdpManager.Application.Common;
using RdpManager.Application.Machines;
using RdpManager.Application.Sessions;
using RdpManager.Presentation.Services;

namespace RdpManager.Presentation.ViewModels;

/// <summary>
/// Master list + actions. All decisions live here; the View only binds. There is no monitor-index
/// logic anywhere — the use case owns matching and this VM reacts to the outcome.
/// </summary>
public sealed partial class MachinesViewModel : ObservableObject
{
    private readonly MachineService _machines;
    private readonly RemoteSessionUseCase _remote;
    private readonly IReachabilityProbe _probe;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;
    private readonly IDispatcherService _dispatcher;
    private readonly Func<MonitorPickerViewModel> _pickerFactory;

    // Machine ids with a live mstsc session; survives list reloads.
    private readonly HashSet<Guid> _activeSessions = new();

    public ObservableCollection<MachineItemViewModel> Machines { get; } = new();

    [ObservableProperty] private MachineItemViewModel? _selected;
    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private bool _isBusy;

    /// <summary>Raised when the flow needs the picker shown (first remote or low-confidence match).</summary>
    public event Action<MonitorPickerViewModel>? ConfigureDisplayRequested;

    public MachinesViewModel(
        MachineService machines, RemoteSessionUseCase remote, IReachabilityProbe probe,
        IDialogService dialogs, IToastService toasts, IDispatcherService dispatcher,
        Func<MonitorPickerViewModel> pickerFactory)
    {
        _machines = machines; _remote = remote; _probe = probe;
        _dialogs = dialogs; _toasts = toasts; _dispatcher = dispatcher; _pickerFactory = pickerFactory;
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct)
    {
        IsBusy = true;
        try
        {
            var all = await _machines.ListAsync(ct);
            var query = Search.Trim();
            var selectedId = Selected?.Id;

            Machines.Clear();
            foreach (var m in all.Where(m => Match(m, query)))
                Machines.Add(new MachineItemViewModel(m) { IsActive = _activeSessions.Contains(m.Id) });

            Selected = Machines.FirstOrDefault(x => x.Id == selectedId) ?? Machines.FirstOrDefault();
        }
        finally { IsBusy = false; }
    }

    partial void OnSearchChanged(string value) => _ = LoadAsync(CancellationToken.None);

    /// <summary>Creates a machine from dialog input (optionally storing a password), then selects it.</summary>
    public async Task AddMachineAsync(CreateMachineRequest request, string? password, CancellationToken ct)
    {
        var result = await _machines.CreateAsync(request, ct);
        if (!result.IsSuccess)
        {
            await _dialogs.ErrorAsync("Couldn't add machine", result.Error!.Message);
            return;
        }
        await SavePasswordIfProvided(result.Value.Id, password, ct);
        await LoadAsync(ct);
        Selected = Machines.FirstOrDefault(m => m.Id == result.Value.Id) ?? Selected;
    }

    /// <summary>Saves edits to the selected machine's basic details (and password if provided).</summary>
    public async Task EditMachineAsync(Guid machineId, CreateMachineRequest request, string? password, CancellationToken ct)
    {
        var result = await _machines.UpdateDetailsAsync(machineId, request, ct);
        if (!result.IsSuccess)
        {
            await _dialogs.ErrorAsync("Couldn't save changes", result.Error!.Message);
            return;
        }
        await SavePasswordIfProvided(machineId, password, ct);
        await LoadAsync(ct);
        Selected = Machines.FirstOrDefault(m => m.Id == machineId) ?? Selected;
    }

    private async Task SavePasswordIfProvided(Guid machineId, string? password, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(password)) return;
        using var secure = new SecureString();
        foreach (var c in password) secure.AppendChar(c);
        secure.MakeReadOnly();
        await _machines.SetPasswordAsync(machineId, secure, ct);
    }

    /// <summary>Re-opens the monitor picker for the selected machine so the user can re-choose screens.</summary>
    public async Task ConfigureDisplayAsync(CancellationToken ct)
    {
        if (Selected is null) return;
        var picker = _pickerFactory();
        await picker.LoadForAsync(Selected.Id, ct);
        ConfigureDisplayRequested?.Invoke(picker);
    }

    [RelayCommand]
    private async Task PingAsync(CancellationToken ct)
    {
        if (Selected is null) return;
        var result = await _probe.CheckAsync(Selected.Model.Host, ct);
        _toasts.Show(result.IsReachable
            ? $"{Selected.Name}: reachable · {result.RoundtripMs} ms"
            : $"{Selected.Name}: unreachable · {result.Detail}");
    }

    [RelayCommand]
    private async Task RemoteAsync(CancellationToken ct)
    {
        if (Selected is null) return;
        if (Selected.Model.Os == Domain.Enums.MachineOs.MacOs)
        {
            // macOS has no RDP server — honest refusal instead of a failing mstsc window.
            _toasts.Show($"{Selected.Name} is a macOS machine — RDP isn't available.");
            return;
        }
        var machineId = Selected.Id;
        var result = await _remote.ExecuteAsync(machineId, ct);
        await HandleOutcomeAsync(machineId, result, ct);
    }

    private async Task HandleOutcomeAsync(Guid machineId, Result<SessionOutcome> result, CancellationToken ct)
    {
        if (!result.IsSuccess)
        {
            var title = result.Error!.Kind == ErrorKind.Unreachable ? "Can't reach host" : "Couldn't start session";
            await _dialogs.ErrorAsync(title, result.Error.Message);
            return;
        }

        var outcome = result.Value;
        if (outcome.Kind == SessionResultKind.NeedsReconfiguration)
        {
            var picker = _pickerFactory();
            await picker.LoadForAsync(machineId, ct);
            ConfigureDisplayRequested?.Invoke(picker);
            return;
        }

        _toasts.Show(outcome.Remapped ? "Re-mapped to your saved layout." : "Session launched.");
        if (outcome.ProcessId is int pid)
            _ = TrackSessionAsync(pid, machineId);
    }

    /// <summary>Marks a machine active while its mstsc process lives, then clears the status.</summary>
    private async Task TrackSessionAsync(int pid, Guid machineId)
    {
        SetActive(machineId, true);
        try
        {
            var process = Process.GetProcessById(pid);
            await process.WaitForExitAsync();
        }
        catch { /* process already gone */ }
        SetActive(machineId, false);
    }

    private void SetActive(Guid machineId, bool active)
    {
        // Marshal to the UI thread — WaitForExitAsync resumes on the thread pool.
        _dispatcher.Post(() =>
        {
            if (active) _activeSessions.Add(machineId); else _activeSessions.Remove(machineId);
            var item = Machines.FirstOrDefault(m => m.Id == machineId);
            if (item is not null) item.IsActive = active;
        });
    }

    [RelayCommand]
    private async Task DeleteAsync(CancellationToken ct)
    {
        if (Selected is null) return;
        if (!await _dialogs.ConfirmDeleteAsync(Selected.Name)) return;
        await _machines.DeleteAsync(Selected.Id, ct);
        await LoadAsync(ct);
    }

    private static bool Match(Domain.Entities.Machine m, string q) =>
        string.IsNullOrEmpty(q)
        || m.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
        || m.Host.Host.Contains(q, StringComparison.OrdinalIgnoreCase)
        || m.Tags.Any(t => t.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
}
