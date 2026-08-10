using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RdpManager.Application.Files;
using RdpManager.Application.Rdp;
using RdpManager.Domain.Enums;
using RdpManager.Presentation.Services;

namespace RdpManager.Presentation.ViewModels;

/// <summary>
/// The Files console engine: dual-pane commander (This PC ⇄ remote host) over RDP drive
/// redirection, with the transfer dock underneath. It no longer owns the machine list — the
/// merged Machines &amp; Files destination pushes the selected machine in via
/// <see cref="SelectedMachine"/>; this type owns everything file-side.
/// </summary>
public sealed partial class FilesViewModel : ObservableObject
{
    private const string DefaultRemotePath = @"C:\";

    private readonly IFileTransferService _transfers;
    private readonly IDriveRedirectionSettings _driveRedirection;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;
    private readonly IDispatcherService _dispatcher;

    // In-app drag payload (set on DragItemsStarting, consumed on Drop).
    private IReadOnlyList<FileEntryViewModel> _dragEntries = Array.Empty<FileEntryViewModel>();
    private TransferEndpoint? _dragSource;

    public FilesViewModel(
        IFileTransferService transfers, IDriveRedirectionSettings driveRedirection,
        IDialogService dialogs, IToastService toasts, IDispatcherService dispatcher)
    {
        _transfers = transfers;
        _driveRedirection = driveRedirection;
        _dialogs = dialogs;
        _toasts = toasts;
        _dispatcher = dispatcher;

        LocalPane = new PaneViewModel(TransferEndpoint.Local, transfers, "This PC",
            "Nothing here. Navigate to a folder that has the files you want to send.");
        RemotePane = new PaneViewModel(TransferEndpoint.Remote, transfers, "Remote",
            "This folder is empty. Drag files here from the left, or select on This PC and press →.");

        Dock = new TransferDockViewModel(transfers, dispatcher);
        Dock.JobCompleted += OnJobCompleted;

        LocalPane.SelectionChanged += () => HasLocalSelection = LocalPane.SelectedEntries.Any(e => e.IsFile);
        RemotePane.SelectionChanged += () => HasRemoteSelection = RemotePane.SelectedEntries.Any(e => e.IsFile);
    }

    public PaneViewModel LocalPane { get; }
    public PaneViewModel RemotePane { get; }
    public TransferDockViewModel Dock { get; }

    [ObservableProperty] private MachineItemViewModel? _selectedMachine;
    [ObservableProperty] private bool _isRedirectionOn;
    [ObservableProperty] private string _redirectionStatusText = "Redirection off";
    [ObservableProperty] private string _connectionText = "Select a machine on the left.";
    [ObservableProperty] private bool _hasLocalSelection;
    [ObservableProperty] private bool _hasRemoteSelection;

    /// <summary>Console = panes + arrows. Off when no machine is selected or redirection is disabled.</summary>
    public bool IsConsoleEnabled => SelectedMachine is not null && IsRedirectionOn;
    public bool IsRedirectionOffForSelected => SelectedMachine is not null && !IsRedirectionOn;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(LocalPane.CurrentPath))
            await LocalPane.NavigateAsync(_transfers.DefaultLocalDirectory);
    }

    partial void OnSelectedMachineChanged(MachineItemViewModel? value) =>
        _ = GuardedMachineChangeAsync(value);

    private async Task GuardedMachineChangeAsync(MachineItemViewModel? value)
    {
        try
        {
            await OnMachineChangedAsync(value);
        }
        catch (Exception ex)
        {
            // Settings/DB hiccups must degrade to a message, never an unhandled crash loop.
            IsRedirectionOn = false;
            NotifyConsoleState();
            _toasts.Show($"Couldn't read this machine's redirection settings: {ex.Message}");
        }
    }

    private async Task OnMachineChangedAsync(MachineItemViewModel? machine)
    {
        if (machine is null)
        {
            IsRedirectionOn = false;
            ConnectionText = "Select a machine on the left — transfers ride its RDP session.";
            NotifyConsoleState();
            return;
        }

        var model = machine.Model;
        ConnectionText = $"Files on {model.Name} · {machine.HostLine}";
        RemotePane.Title = model.Name;

        var redirection = await _driveRedirection.GetAsync(machine.Id, CancellationToken.None);
        ApplyRedirectionState(redirection);

        if (IsRedirectionOn)
            await RemotePane.NavigateAsync(DefaultRemotePath);
        NotifyConsoleState();
    }

    private void ApplyRedirectionState(DriveRedirection redirection)
    {
        IsRedirectionOn = redirection.IsEnabled;
        RedirectionStatusText = redirection.IsEnabled ? "Redirection live" : "Redirection off";
        NotifyConsoleState();
    }

    private void NotifyConsoleState()
    {
        OnPropertyChanged(nameof(IsConsoleEnabled));
        OnPropertyChanged(nameof(IsRedirectionOffForSelected));
    }

    // ── Drive redirection dialog plumbing ───────────────────────────────────

    public Task<DriveRedirection> GetRedirectionAsync() =>
        SelectedMachine is null
            ? Task.FromResult(DriveRedirection.None)
            : _driveRedirection.GetAsync(SelectedMachine.Id, CancellationToken.None);

    public async Task<IReadOnlyList<FileSystemEntry>> GetLocalRootsAsync()
    {
        var roots = await _transfers.ListLocalRootsAsync(CancellationToken.None);
        return roots.IsSuccess ? roots.Value : Array.Empty<FileSystemEntry>();
    }

    /// <summary>Persists the dialog's outcome. Turning redirection off honestly closes the console.</summary>
    public async Task ApplyRedirectionAsync(DriveRedirection value)
    {
        if (SelectedMachine is null) return;
        await _driveRedirection.SetAsync(SelectedMachine.Id, value, CancellationToken.None);
        ApplyRedirectionState(value);

        if (IsRedirectionOn && string.IsNullOrEmpty(RemotePane.CurrentPath))
            await RemotePane.NavigateAsync(DefaultRemotePath);
        _toasts.Show(IsRedirectionOn
            ? "Drive redirection updated. It applies to the next session you launch."
            : "Drive redirection is off — the Files console is disabled for this machine.");
    }

    // ── Transfers ───────────────────────────────────────────────────────────

    [RelayCommand]
    public Task SendAsync() => TransferSelectionAsync(TransferDirection.Upload);

    [RelayCommand]
    public Task GetAsync() => TransferSelectionAsync(TransferDirection.Download);

    private Task TransferSelectionAsync(TransferDirection direction)
    {
        var source = direction == TransferDirection.Upload ? LocalPane : RemotePane;
        return TransferAsync(direction, source.SelectedEntries);
    }

    public void BeginDrag(TransferEndpoint source, IEnumerable<FileEntryViewModel> entries)
    {
        _dragSource = source;
        _dragEntries = entries.Where(e => e.IsFile).ToList();
    }

    /// <summary>Drop on the opposite pane. A drop on the drag's own pane is a no-op.</summary>
    public async Task DropOnAsync(TransferEndpoint target)
    {
        if (_dragSource is null || _dragSource == target || _dragEntries.Count == 0) return;
        var direction = target == TransferEndpoint.Remote ? TransferDirection.Upload : TransferDirection.Download;
        var entries = _dragEntries;
        _dragSource = null;
        _dragEntries = Array.Empty<FileEntryViewModel>();
        await TransferAsync(direction, entries);
    }

    public async Task TransferAsync(TransferDirection direction, IReadOnlyList<FileEntryViewModel> entries)
    {
        if (!IsConsoleEnabled) return;

        var files = entries.Where(e => e.IsFile).ToList();
        if (files.Count == 0)
        {
            _toasts.Show("Select a file first, then choose a direction.");
            return;
        }

        var destination = direction == TransferDirection.Upload ? RemotePane : LocalPane;
        if (string.IsNullOrEmpty(destination.CurrentPath))
        {
            _toasts.Show("Open a destination folder first.");
            return;
        }

        var requests = files
            .Select(f => new TransferRequest(direction, f.Entry.FullPath, destination.CurrentPath))
            .ToList();

        var plan = await _transfers.PlanAsync(requests, CancellationToken.None);
        if (!plan.IsSuccess)
        {
            await _dialogs.ErrorAsync("Couldn't start the transfer", plan.Error!.Message);
            return;
        }

        foreach (var ready in plan.Value.Ready)
            await _transfers.EnqueueAsync(ready, CancellationToken.None);

        await ResolveConflictsAsync(plan.Value.Conflicts);

        var queued = plan.Value.Ready.Count + plan.Value.Conflicts.Count;
        if (queued > 0)
        {
            Dock.ShowQueueCommand.Execute(null);
            _toasts.Show($"{queued} {(queued == 1 ? "file" : "files")} {(direction == TransferDirection.Upload ? "→ remote" : "← local")} queued.");
        }
    }

    /// <summary>Walks the conflicts, asking once per file unless the user ticked "apply to all".</summary>
    private async Task ResolveConflictsAsync(IReadOnlyList<NameConflict> conflicts)
    {
        OverwriteDecision? applyToAll = null;
        foreach (var conflict in conflicts)
        {
            OverwriteDecision decision;
            if (applyToAll is { } all)
            {
                decision = all;
            }
            else
            {
                var choice = await _dialogs.OverwriteAsync(
                    conflict.Request.FileName,
                    TransferFormat.Bytes(conflict.Existing.SizeBytes),
                    TransferFormat.Bytes(conflict.Source.SizeBytes));
                decision = choice?.Decision ?? OverwriteDecision.Skip;
                if (choice is { ApplyToAll: true }) applyToAll = decision;
            }

            var result = await _transfers.EnqueueResolvedAsync(conflict, decision, CancellationToken.None);
            if (!result.IsSuccess)
                await _dialogs.ErrorAsync("Couldn't queue the file", result.Error!.Message);
        }
    }

    private async void OnJobCompleted(TransferItem item)
    {
        try
        {
            await RefreshDestinationAsync(item);
        }
        catch (Exception)
        {
            // A failed listing refresh is cosmetic; the pane's own error states cover the rest.
        }
    }

    private Task RefreshDestinationAsync(TransferItem item)
    {
        // A finished upload changes the remote listing; a finished download changes the local one.
        var pane = item.Job.Direction == TransferDirection.Upload ? RemotePane : LocalPane;
        var destinationDir = TransferPath.GetParent(item.DestinationPath);
        if (destinationDir is not null &&
            string.Equals(pane.CurrentPath, destinationDir, StringComparison.OrdinalIgnoreCase))
        {
            return pane.RefreshAsync();
        }
        return Task.CompletedTask;
    }

    // ── Row context-menu actions (shared by both panes) ─────────────────────

    public async Task NewFolderAsync(PaneViewModel pane)
    {
        var name = await _dialogs.PromptTextAsync("New folder", "Folder name", "New folder");
        if (string.IsNullOrWhiteSpace(name)) return;
        var error = await pane.CreateFolderAsync(name);
        if (error is not null) await _dialogs.ErrorAsync("Couldn't create the folder", error.Message);
    }

    public async Task RenameAsync(PaneViewModel pane, FileEntryViewModel entry)
    {
        var name = await _dialogs.PromptTextAsync($"Rename “{entry.Name}”", "New name", entry.Name);
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, entry.Name, StringComparison.Ordinal)) return;
        var error = await pane.RenameEntryAsync(entry, name);
        if (error is not null) await _dialogs.ErrorAsync("Couldn't rename", error.Message);
    }

    public async Task DeleteAsync(PaneViewModel pane, FileEntryViewModel entry)
    {
        var what = entry.IsFolder ? "folder and everything in it" : "file";
        if (!await _dialogs.ConfirmAsync($"Delete “{entry.Name}”?",
                $"This permanently deletes the {what}. This can't be undone.", "Delete", "Cancel"))
            return;
        var error = await pane.DeleteEntryAsync(entry);
        if (error is not null) await _dialogs.ErrorAsync("Couldn't delete", error.Message);
    }

    public async Task ShowPropertiesAsync(FileEntryViewModel entry)
    {
        var kind = entry.IsFolder ? "Folder" : "File";
        var size = entry.IsFolder ? "" : $"\nSize: {entry.SizeText}";
        await _dialogs.InfoAsync(entry.Name,
            $"{kind}{size}\nModified: {entry.ModifiedText}\nPath: {entry.Entry.FullPath}");
    }
}
