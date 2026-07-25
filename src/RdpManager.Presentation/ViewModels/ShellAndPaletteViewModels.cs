using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RdpManager.Application.Machines;
using RdpManager.Application.Sessions;
using RdpManager.Domain.Entities;

namespace RdpManager.Presentation.ViewModels;

public enum ShellDestination { Machines, Groups, Activity, Settings }

public sealed partial class ShellViewModel : ObservableObject
{
    [ObservableProperty] private ShellDestination _current = ShellDestination.Machines;
    [ObservableProperty] private bool _isPaletteOpen;

    public MachinesViewModel Machines { get; }

    public ShellViewModel(MachinesViewModel machines) => Machines = machines;

    [RelayCommand] private void Navigate(ShellDestination d) => Current = d;
    [RelayCommand] private void TogglePalette() => IsPaletteOpen = !IsPaletteOpen;
}

/// <summary>The velocity layer: type → Enter remotes. Keyboard-first, Raycast-style.</summary>
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    private readonly MachineService _machines;
    private readonly RemoteSessionUseCase _remote;
    private IReadOnlyList<Machine> _all = System.Array.Empty<Machine>();

    public ObservableCollection<Machine> Results { get; } = new();
    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private Machine? _highlighted;

    public CommandPaletteViewModel(MachineService machines, RemoteSessionUseCase remote)
    {
        _machines = machines; _remote = remote;
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        _all = await _machines.ListAsync(ct);
        Filter();
    }

    partial void OnQueryChanged(string value) => Filter();

    private void Filter()
    {
        var q = Query.Trim();
        Results.Clear();
        foreach (var m in _all.Where(m => string.IsNullOrEmpty(q)
                     || m.Name.Contains(q, System.StringComparison.OrdinalIgnoreCase)
                     || m.Host.Host.Contains(q, System.StringComparison.OrdinalIgnoreCase))
                 .Take(6))
            Results.Add(m);
        Highlighted = Results.FirstOrDefault();
    }

    [RelayCommand]
    private async Task RunAsync(CancellationToken ct)
    {
        if (Highlighted is null) return;
        await _remote.ExecuteAsync(Highlighted.Id, ct);
    }
}
