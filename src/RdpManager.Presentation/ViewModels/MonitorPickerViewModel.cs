using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RdpManager.Application.Abstractions;
using RdpManager.Application.Display;
using RdpManager.Application.Machines;
using RdpManager.Domain.Entities;
using RdpManager.Presentation.Services;

namespace RdpManager.Presentation.ViewModels;

/// <summary>One selectable monitor tile — positions, never indexes, are what the user sees.</summary>
public sealed partial class MonitorTileViewModel : ObservableObject
{
    public required MonitorInfo Live { get; init; }
    public string Label { get; init; } = "";
    public string Resolution => $"{Live.Geometry.Width}×{Live.Geometry.Height}";
    public bool IsPrimary => Live.IsPrimary;
    public double X => Live.Geometry.X;
    public double Y => Live.Geometry.Y;
    [ObservableProperty] private bool _isSelected;
}

public sealed partial class MonitorPickerViewModel : ObservableObject
{
    private readonly IDisplayTopologyProvider _topology;
    private readonly MachineService _machines;
    private readonly IToastService _toasts;
    private Guid _machineId;

    public ObservableCollection<MonitorTileViewModel> Monitors { get; } = new();
    [ObservableProperty] private bool _isSingleDisplayHost;

    /// <summary>Raised when the profile is saved so the host dialog can close.</summary>
    public event Action? Saved;

    public MonitorPickerViewModel(IDisplayTopologyProvider topology, MachineService machines, IToastService toasts)
    {
        _topology = topology; _machines = machines; _toasts = toasts;
    }

    public async Task LoadForAsync(Guid machineId, CancellationToken ct)
    {
        _machineId = machineId;
        var topo = await _topology.GetCurrentAsync(ct);
        IsSingleDisplayHost = topo.Count <= 1;

        Monitors.Clear();
        foreach (var (m, label) in Label(topo))
            Monitors.Add(new MonitorTileViewModel { Live = m, Label = label, IsSelected = m.IsPrimary });
    }

    [RelayCommand]
    private void ToggleMonitor(MonitorTileViewModel tile) => tile.IsSelected = !tile.IsSelected;

    [RelayCommand]
    private void UseAllScreens()
    {
        foreach (var t in Monitors) t.IsSelected = true;
    }

    public bool CanSave => Monitors.Any(m => m.IsSelected);

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct)
    {
        var selected = Monitors.Where(m => m.IsSelected).ToList();
        if (selected.Count == 0) return;

        // Persist as fingerprints (identity + geometry) — never the OS index.
        var fingerprints = selected.Select((t, i) => new MonitorFingerprint(
            i, t.Live.Geometry, t.Live.DevicePath, t.Live.IsPrimary,
            t.Live.EdidManufacturer, t.Live.EdidProductCode, t.Live.EdidSerial)).ToList();

        var result = await _machines.ConfigureDisplayAsync(_machineId, fingerprints, ct);
        if (result.IsSuccess)
        {
            _toasts.Show("Monitor positions saved.");
            Saved?.Invoke();
        }
    }

    /// <summary>Derives human position labels (Left/Center/Right/Top…) from geometry.</summary>
    private static IEnumerable<(MonitorInfo, string)> Label(DisplayTopology topo)
    {
        var ordered = topo.Monitors.OrderBy(m => m.Geometry.X).ThenBy(m => m.Geometry.Y).ToList();
        foreach (var m in topo.Monitors)
        {
            string label;
            if (m.IsPrimary) label = "Center (primary)";
            else
            {
                var rank = ordered.IndexOf(m);
                label = rank == 0 ? "Left" : rank == ordered.Count - 1 ? "Right" : "Middle";
            }
            yield return (m, label);
        }
    }
}
