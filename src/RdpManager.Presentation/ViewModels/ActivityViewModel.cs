using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RdpManager.Application.History;
using RdpManager.Application.Machines;
using RdpManager.Domain.Entities;
using RdpManager.Domain.Enums;

namespace RdpManager.Presentation.ViewModels;

/// <summary>One row in the activity log.</summary>
public sealed class ActivityItemViewModel
{
    public ActivityItemViewModel(SessionHistoryEntry e, string machineName)
    {
        MachineName = machineName;
        When = e.OccurredAt.ToLocalTime().ToString("MMM d · HH:mm");
        Detail = e.Detail ?? string.Empty;
        (EventText, Glyph) = Describe(e.Type);
    }

    public string When { get; }
    public string MachineName { get; }
    public string EventText { get; }
    public string Detail { get; }
    public string Glyph { get; }

    // Segoe MDL2 Assets glyphs.
    private static (string text, string glyph) Describe(HistoryEventType type) => type switch
    {
        HistoryEventType.Session => ("Remote session", ""),
        HistoryEventType.LayoutRemapped => ("Layout re-mapped", ""),
        HistoryEventType.Ping => ("Ping", ""),
        HistoryEventType.PreflightBlocked => ("Blocked — unreachable", ""),
        HistoryEventType.DisplayConfigured => ("Display configured", ""),
        _ => ("Event", ""),
    };
}

public sealed partial class ActivityViewModel : ObservableObject
{
    private readonly HistoryService _history;
    private readonly MachineService _machines;

    public ObservableCollection<ActivityItemViewModel> Items { get; } = new();
    [ObservableProperty] private bool _isEmpty;

    public ActivityViewModel(HistoryService history, MachineService machines)
    {
        _history = history;
        _machines = machines;
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct)
    {
        var names = (await _machines.ListAsync(ct)).ToDictionary(m => m.Id, m => m.Name);
        var entries = await _history.RecentAsync(200, ct);

        Items.Clear();
        foreach (var e in entries)
            Items.Add(new ActivityItemViewModel(e, names.GetValueOrDefault(e.MachineId, "(deleted machine)")));

        IsEmpty = Items.Count == 0;
    }
}
