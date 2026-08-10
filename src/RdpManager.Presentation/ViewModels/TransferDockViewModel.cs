using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RdpManager.Application.Files;
using RdpManager.Domain.Enums;
using RdpManager.Presentation.Services;

namespace RdpManager.Presentation.ViewModels;

/// <summary>One row in the transfer dock's Queue tab. Updated in place so progress doesn't flicker.</summary>
public sealed partial class TransferJobViewModel : ObservableObject
{
    private readonly IFileTransferService _service;

    public TransferJobViewModel(TransferItem item, IFileTransferService service)
    {
        _service = service;
        Id = item.Job.Id;
        FileName = item.Job.FileName;
        DirectionGlyph = item.Job.Direction == TransferDirection.Upload ? "" : "";
        Update(item);
    }

    public Guid Id { get; }
    public string FileName { get; }
    public string DirectionGlyph { get; }

    [ObservableProperty] private string _metaText = string.Empty;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isFailed;
    [ObservableProperty] private bool _isCompleted;
    [ObservableProperty] private bool _showRetry;
    [ObservableProperty] private bool _showCancel;
    /// <summary>"Active" | "Done" | "Failed" | "Muted" — drives the bar/status brush via converter.</summary>
    [ObservableProperty] private string _barState = "Active";

    public void Update(TransferItem item)
    {
        var job = item.Job;
        MetaText = $"{TransferFormat.Bytes(job.TotalBytes)} · {(job.Direction == TransferDirection.Upload ? "to remote" : "to local")}";
        ProgressPercent = job.TotalBytes <= 0
            ? (job.Status == TransferStatus.Completed ? 100 : 0)
            : Math.Clamp(job.BytesTransferred * 100.0 / job.TotalBytes, 0, 100);

        IsActive = job.Status == TransferStatus.Active;
        IsFailed = job.Status == TransferStatus.Failed;
        IsCompleted = job.Status == TransferStatus.Completed;
        ShowRetry = job.Status is TransferStatus.Failed or TransferStatus.Cancelled;
        ShowCancel = job.Status is TransferStatus.Active or TransferStatus.Queued;

        BarState = job.Status switch
        {
            TransferStatus.Completed => "Done",
            TransferStatus.Failed => "Failed",
            TransferStatus.Skipped or TransferStatus.Cancelled => "Muted",
            _ => "Active",
        };

        StatusText = job.Status switch
        {
            TransferStatus.Queued => "Queued",
            TransferStatus.Active when item.BytesPerSecond is > 0 =>
                item.EstimatedRemaining is { } eta
                    ? $"{TransferFormat.Speed(item.BytesPerSecond.Value)} · {TransferFormat.Eta(eta)}"
                    : TransferFormat.Speed(item.BytesPerSecond.Value),
            TransferStatus.Active => $"{(int)ProgressPercent}%",
            TransferStatus.Completed => "Complete",
            TransferStatus.Failed => job.ErrorReason ?? "Failed",
            TransferStatus.Skipped => "Skipped",
            TransferStatus.Cancelled => "Cancelled",
            _ => string.Empty,
        };
    }

    [RelayCommand] private void Retry() => _service.Retry(Id);
    [RelayCommand] private void Cancel() => _service.Cancel(Id);
}

/// <summary>One row in the History tab.</summary>
public sealed class TransferHistoryItemViewModel
{
    public TransferHistoryItemViewModel(TransferRecord record)
    {
        FileName = record.Job.FileName;
        DirectionGlyph = record.Job.Direction == TransferDirection.Upload ? "↑" : "↓";
        SizeText = TransferFormat.Bytes(record.Job.TotalBytes);
        WhenText = TransferFormat.When(record.FinishedAt);
        ResultText = record.Job.Status switch
        {
            TransferStatus.Completed => "Complete",
            TransferStatus.Skipped => "Skipped",
            TransferStatus.Cancelled => "Cancelled",
            _ => record.Job.ErrorReason ?? "Failed",
        };
        IsCompleted = record.Job.Status == TransferStatus.Completed;
        IsFailed = record.Job.Status == TransferStatus.Failed;
        ResultState = record.Job.Status switch
        {
            TransferStatus.Completed => "Done",
            TransferStatus.Failed => "Failed",
            _ => "Muted",
        };
    }

    public string FileName { get; }
    public string DirectionGlyph { get; }
    public string SizeText { get; }
    public string WhenText { get; }
    public string ResultText { get; }
    public bool IsCompleted { get; }
    public bool IsFailed { get; }
    public string ResultState { get; }
}

/// <summary>
/// The persistent transfer dock: Queue and History tabs over <see cref="IFileTransferService"/>.
/// Nothing silently succeeds or fails — every job is visible here until the user clears it.
/// </summary>
public sealed partial class TransferDockViewModel : ObservableObject
{
    private readonly IFileTransferService _service;
    private readonly IDispatcherService _dispatcher;
    private readonly Dictionary<Guid, TransferStatus> _lastStatuses = new();
    private int _refreshQueued; // coalesces bursts of progress events

    public TransferDockViewModel(IFileTransferService service, IDispatcherService dispatcher)
    {
        _service = service;
        _dispatcher = dispatcher;
        _service.TransfersChanged += OnTransfersChanged;
        Refresh();
    }

    public ObservableCollection<TransferJobViewModel> Jobs { get; } = new();
    public ObservableCollection<TransferHistoryItemViewModel> HistoryItems { get; } = new();

    [ObservableProperty] private bool _isQueueTabSelected = true;
    [ObservableProperty] private int _activeCount;
    [ObservableProperty] private string _summaryText = "Idle";
    [ObservableProperty] private bool _isQueueEmpty = true;

    public bool IsHistoryTabSelected => !IsQueueTabSelected;

    public string QueueTabText => ActiveCount > 0 ? $"Queue ({ActiveCount})" : "Queue";

    /// <summary>Raised (on the UI thread) when a job reaches Completed — panes refresh their listings.</summary>
    public event Action<TransferItem>? JobCompleted;

    [RelayCommand] private void ShowQueue() => IsQueueTabSelected = true;
    [RelayCommand] private void ShowHistory() => IsQueueTabSelected = false;

    partial void OnIsQueueTabSelectedChanged(bool value) => OnPropertyChanged(nameof(IsHistoryTabSelected));

    partial void OnActiveCountChanged(int value) => OnPropertyChanged(nameof(QueueTabText));

    [RelayCommand]
    private void ClearFinished()
    {
        _service.ClearFinished();
        Refresh();
    }

    private void OnTransfersChanged(object? sender, EventArgs e)
    {
        // The service raises on its pump thread, per chunk. Coalesce and marshal.
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1) return;
        _dispatcher.Post(() =>
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
            Refresh();
        });
    }

    private void Refresh()
    {
        var queue = _service.Queue;

        // Update in place; add/remove only when the set changed.
        var byId = Jobs.ToDictionary(j => j.Id);
        var seen = new HashSet<Guid>();
        for (var i = 0; i < queue.Count; i++)
        {
            var item = queue[i];
            seen.Add(item.Job.Id);
            if (byId.TryGetValue(item.Job.Id, out var vm)) vm.Update(item);
            else Jobs.Insert(Math.Min(i, Jobs.Count), new TransferJobViewModel(item, _service));

            if (_lastStatuses.TryGetValue(item.Job.Id, out var previous)
                && previous != TransferStatus.Completed
                && item.Job.Status == TransferStatus.Completed)
            {
                JobCompleted?.Invoke(item);
            }
            _lastStatuses[item.Job.Id] = item.Job.Status;
        }
        for (var i = Jobs.Count - 1; i >= 0; i--)
            if (!seen.Contains(Jobs[i].Id)) Jobs.RemoveAt(i);

        var history = _service.History;
        if (history.Count != HistoryItems.Count)
        {
            HistoryItems.Clear();
            foreach (var record in history)
                HistoryItems.Add(new TransferHistoryItemViewModel(record));
        }

        ActiveCount = queue.Count(i => i.Job.Status is TransferStatus.Active or TransferStatus.Queued);
        var done = queue.Count(i => i.Job.Status == TransferStatus.Completed);
        SummaryText = ActiveCount > 0
            ? $"{ActiveCount} transferring · {done} done"
            : done > 0 ? $"{done} complete" : "Idle";
        IsQueueEmpty = queue.Count == 0;
    }
}
