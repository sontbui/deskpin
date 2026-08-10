using System;
using CommunityToolkit.Mvvm.ComponentModel;
using RdpManager.Domain.Entities;

namespace RdpManager.Presentation.ViewModels;

/// <summary>
/// Row wrapper around a <see cref="Machine"/> so the list can show live per-machine state
/// (active session, reachability, latency) without mutating the domain entity.
/// </summary>
public sealed partial class MachineItemViewModel : ObservableObject
{
    public Machine Model { get; }

    public MachineItemViewModel(Machine model) => Model = model;

    public Guid Id => Model.Id;
    public string Name => Model.Name;
    public string HostText => Model.Host.Host;

    /// <summary>"user@host" line for the master list (host only when no username is stored).</summary>
    public string HostLine => string.IsNullOrEmpty(Model.Username)
        ? Model.Host.ToString()
        : $"{Model.Username}@{Model.Host}";

    /// <summary>True while an mstsc session launched from here is still running.</summary>
    [ObservableProperty] private bool _isActive;

    /// <summary>Reachability dot: green when the last probe answered.</summary>
    [ObservableProperty] private bool _isUp;

    /// <summary>Right-aligned latency ("18 ms"), or "—" when down/not probed yet.</summary>
    [ObservableProperty] private string _latencyText = "—";
}
