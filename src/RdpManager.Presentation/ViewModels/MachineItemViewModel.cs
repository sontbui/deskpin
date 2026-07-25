using System;
using CommunityToolkit.Mvvm.ComponentModel;
using RdpManager.Domain.Entities;

namespace RdpManager.Presentation.ViewModels;

/// <summary>
/// Row wrapper around a <see cref="Machine"/> so the list can show live per-machine state
/// (like an active remote session) without mutating the domain entity.
/// </summary>
public sealed partial class MachineItemViewModel : ObservableObject
{
    public Machine Model { get; }

    public MachineItemViewModel(Machine model) => Model = model;

    public Guid Id => Model.Id;
    public string Name => Model.Name;
    public string HostText => Model.Host.Host;

    /// <summary>True while an mstsc session launched from here is still running.</summary>
    [ObservableProperty] private bool _isActive;
}
