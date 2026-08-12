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
    private string _defaultRemotePath = @"C:\";

    private readonly IFileTransferService _transfers;
    private readonly IRemoteConnectionFactory _connections;
    private readonly IDriveRedirectionSettings _driveRedirection;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;
    private readonly IDispatcherService _dispatcher;

    // In-app drag payload (set on DragItemsStarting, consumed on Drop).
    private IReadOnlyList<FileEntryViewModel> _dragEntries = Array.Empty<FileEntryViewModel>();
    private TransferEndpoint? _dragSource;

    public FilesViewModel(
        IFileTransferService transfers, IRemoteConnectionFactory connections,
        IDriveRedirectionSettings driveRedirection,
        IDialogService dialogs, IToastService toasts, IDispatcherService dispatcher)
    {
        _transfers = transfers;
        _connections = connections;
        _driveRedirection = driveRedirection;
        _dialogs = dialogs;
        _toasts = toasts;
        _dispatcher = dispatcher;

        LocalPane = new PaneViewModel(TransferEndpoint.Local, transfers, "This PC",
            "Nothing here. Navigate to a folder that has the files you want to send.");
        RemotePane = new PaneViewModel(TransferEndpoint.Remote, transfers, "Remote",
            "This folder is empty. Drag files here from the left, or select on This PC and press →.");
        RemotePane.PathModel = transfers.RemotePathModel; // remote is always SFTP → POSIX

        Dock = new TransferDockViewModel(transfers, dispatcher);
        Dock.JobCompleted += OnJobCompleted;

        LocalPane.SelectionChanged += () => HasLocalSelection = LocalPane.SelectedEntries.Any(e => e.IsFile);
        RemotePane.SelectionChanged += () => HasRemoteSelection = RemotePane.SelectedEntries.Any(e => e.IsFile);
    }

    public PaneViewModel LocalPane { get; }
    public PaneViewModel RemotePane { get; }
    public TransferDockViewModel Dock { get; }

    [ObservableProperty] private MachineItemViewModel? _selectedMachine;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _connectionStatusText = "Not connected";
    [ObservableProperty] private string _connectionText = "Select a machine on the left.";
    [ObservableProperty] private string _blockedText = string.Empty;
    [ObservableProperty] private bool _hasLocalSelection;
    [ObservableProperty] private bool _hasRemoteSelection;

    /// <summary>Console = panes + arrows. Enabled once the SFTP session to the selected machine is up.</summary>
    public bool IsConsoleEnabled => SelectedMachine is not null && IsConnected;
    /// <summary>A machine is selected but not connected — show the reason overlay.</summary>
    public bool IsConsoleBlocked => SelectedMachine is not null && !IsConnected;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(LocalPane.CurrentPath))
            await LocalPane.NavigateAsync(_transfers.DefaultLocalDirectory);
    }

    partial void OnSelectedMachineChanged(MachineItemViewModel? value) =>
        _ = GuardedMachineChangeAsync(value);

    /// <summary>Re-runs the machine-change flow (reconnect SFTP + reload remote pane) for the current selection.</summary>
    public Task RefreshForSelectedAsync() => GuardedMachineChangeAsync(SelectedMachine);

    private async Task GuardedMachineChangeAsync(MachineItemViewModel? value)
    {
        try
        {
            await OnMachineChangedAsync(value);
        }
        catch (Exception ex)
        {
            // Any hiccup degrades to a message, never an unhandled crash loop.
            IsConnected = false;
            ConnectionStatusText = "Not connected";
            BlockedText = $"Couldn't connect: {ex.Message}";
            NotifyConsoleState();
        }
    }

    private async Task OnMachineChangedAsync(MachineItemViewModel? machine)
    {
        if (machine is null)
        {
            _transfers.SetRemoteTarget(null, null);
            IsConnected = false;
            ConnectionStatusText = "Not connected";
            ConnectionText = "Select a machine on the left — files are browsed over SFTP.";
            NotifyConsoleState();
            return;
        }

        var model = machine.Model;
        ConnectionText = $"Files on {model.Name} \u00b7 {machine.HostLine} (SFTP :{model.SshPort})";
        RemotePane.Title = model.Name;
        RemotePane.PathModel = _transfers.RemotePathModel; // POSIX for SFTP

        IsConnected = false;
        NotifyConsoleState();

        // Build the SFTP connection (reveals the DPAPI secret only transiently).
        var info = await _connections.CreateAsync(machine.Id, CancellationToken.None);
        if (!ReferenceEquals(SelectedMachine, machine)) return; // user switched away \u2014 drop stale result
        if (info is null || info.Secret is null)
        {
            ConnectionStatusText = "Not connected";
            BlockedText = $"Add {model.Name}\u2019s username and password in Edit so Deskpin can sign in over SFTP.";
            NotifyConsoleState();
            return;
        }

        _transfers.SetRemoteTarget(info.Connection, info.Secret);
        var home = await _transfers.GetRemoteHomeAsync(CancellationToken.None);
        info.Secret.Dispose();

        if (!ReferenceEquals(SelectedMachine, machine)) return; // switched away during connect \u2014 ignore
        if (!home.IsSuccess)
        {
            ConnectionStatusText = "Not connected";
            BlockedText = home.Error!.Message;
            NotifyConsoleState();
            return;
        }

        IsConnected = true;
        ConnectionStatusText = "SFTP connected";
        _defaultRemotePath = string.IsNullOrWhiteSpace(home.Value) ? "/" : home.Value!;
        await RemotePane.NavigateAsync(_defaultRemotePath); // lands in the user's home, WinSCP-style
        NotifyConsoleState();
    }

    private void NotifyConsoleState()
    {
        OnPropertyChanged(nameof(IsConsoleEnabled));
        OnPropertyChanged(nameof(IsConsoleBlocked));
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

    /// <summary>Persists the drive-redirection choice for the RDP session (independent of SFTP file browsing).</summary>
    public async Task ApplyRedirectionAsync(DriveRedirection value)
    {
        if (SelectedMachine is null) return;
        await _driveRedirection.SetAsync(SelectedMachine.Id, value, CancellationToken.None);
        _toasts.Show("Drive redirection updated. It applies to the next RDP session you launch.");
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
                    conflict.Source.Name,
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
        var destinationDir = pane.PathModel.GetParent(item.DestinationPath);
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
