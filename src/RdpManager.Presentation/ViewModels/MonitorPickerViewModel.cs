using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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

/// <summary>One selectable monitor tile. Positions — never indexes — are what the user sees.</summary>
public sealed partial class MonitorTileViewModel : ObservableObject
{
    public required MonitorInfo Live { get; init; }
    public string Label { get; init; } = "";
    public string Resolution => $"{Live.Geometry.Width}×{Live.Geometry.Height}";
    public double X => Live.Geometry.X;

    /// <summary>Selected to be part of the remote session.</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>The one selected monitor that becomes the remote session's PRIMARY (listed first).</summary>
    [ObservableProperty] private bool _isSessionPrimary;
}

public sealed partial class MonitorPickerViewModel : ObservableObject
{
    private readonly IDisplayTopologyProvider _topology;
    private readonly MachineService _machines;
    private readonly IToastService _toasts;
    private Guid _machineId;

    public ObservableCollection<MonitorTileViewModel> Monitors { get; } = new();
    [ObservableProperty] private bool _isSingleDisplayHost;

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

        foreach (var t in Monitors) t.PropertyChanged -= OnTileChanged;
        Monitors.Clear();

        foreach (var (m, label) in Label(topo))
        {
            var tile = new MonitorTileViewModel { Live = m, Label = label, IsSelected = m.IsPrimary };
            tile.PropertyChanged += OnTileChanged;
            Monitors.Add(tile);
        }
        EnsureSessionPrimary();
    }

    private void OnTileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MonitorTileViewModel.IsSelected))
            EnsureSessionPrimary();
    }

    // Exactly one selected monitor must be the session primary; default to the left-most selected.
    private void EnsureSessionPrimary()
    {
        var selected = Monitors.Where(m => m.IsSelected).ToList();
        foreach (var m in Monitors)
            if (m.IsSessionPrimary && !m.IsSelected) m.IsSessionPrimary = false;

        if (selected.Count > 0 && !selected.Any(m => m.IsSessionPrimary))
        {
            var leftMost = selected.OrderBy(m => m.X).First();
            leftMost.IsSessionPrimary = true;
        }
    }

    /// <summary>Marks a selected monitor as the remote session's primary.</summary>
    public void SetSessionPrimary(MonitorTileViewModel tile)
    {
        if (tile is null || !tile.IsSelected) return;
        foreach (var m in Monitors) m.IsSessionPrimary = ReferenceEquals(m, tile);
    }

    [RelayCommand]
    private void UseAllScreens()
    {
        foreach (var t in Monitors) t.IsSelected = true;
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct)
    {
        var selected = Monitors.Where(m => m.IsSelected).ToList();
        if (selected.Count == 0) return;

        // The session primary goes FIRST — RDP treats the first entry of selectedmonitors as the
        // remote primary. The rest follow left-to-right. Physical-primary is irrelevant here.
        var primary = selected.FirstOrDefault(m => m.IsSessionPrimary) ?? selected.OrderBy(m => m.X).First();
        var ordered = new List<MonitorTileViewModel> { primary };
        ordered.AddRange(selected.Where(m => !ReferenceEquals(m, primary)).OrderBy(m => m.X));

        var fingerprints = ordered.Select((t, i) => new MonitorFingerprint(
            i, t.Live.Geometry, t.Live.DevicePath, t.Live.IsPrimary,
            t.Live.EdidManufacturer, t.Live.EdidProductCode, t.Live.EdidSerial)).ToList();

        var result = await _machines.ConfigureDisplayAsync(_machineId, fingerprints, ct);
        if (result.IsSuccess)
        {
            _toasts.Show($"Saved {fingerprints.Count} screen(s).");
            Saved?.Invoke();
        }
    }

    /// <summary>Orders monitors left-to-right by real X and labels them by position (not by primary).</summary>
    private static IEnumerable<(MonitorInfo, string)> Label(DisplayTopology topo)
    {
        var ordered = topo.Monitors.OrderBy(m => m.Geometry.X).ThenBy(m => m.Geometry.Y).ToList();
        var n = ordered.Count;
        for (var i = 0; i < n; i++)
        {
            var m = ordered[i];
            var pos = n == 1 ? "Display"
                : i == 0 ? "Left"
                : i == n - 1 ? "Right"
                : n == 3 ? "Center" : $"Middle {i}";
            yield return (m, m.IsPrimary ? $"{pos} · main" : pos);
        }
    }
}
