using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RdpManager.Application.Common;
using RdpManager.Application.Files;

namespace RdpManager.Presentation.ViewModels;

/// <summary>One row in a commander pane.</summary>
public sealed class FileEntryViewModel
{
    public FileEntryViewModel(FileSystemEntry entry)
    {
        Entry = entry;
        SizeText = entry.IsDirectory ? "—" : TransferFormat.Bytes(entry.SizeBytes);
        ModifiedText = entry.IsDirectory && entry.ModifiedAt is null ? "Folder" : TransferFormat.Modified(entry.ModifiedAt);
    }

    public FileSystemEntry Entry { get; }
    public string Name => Entry.Name;
    public bool IsFolder => Entry.IsDirectory;
    public bool IsFile => !Entry.IsDirectory;
    public string SizeText { get; }
    public string ModifiedText { get; }
    /// <summary>Segoe Fluent/MDL2 glyph: folder or plain document.</summary>
    public string Glyph => Entry.IsDirectory ? "" : "";
}

/// <summary>One clickable segment in a pane's breadcrumb.</summary>
public sealed class BreadcrumbItemViewModel
{
    public BreadcrumbItemViewModel(BreadcrumbSegment segment, bool isLast)
    {
        Label = segment.Label;
        Path = segment.Path;
        IsLast = isLast;
    }

    public string Label { get; }
    public string Path { get; }
    public bool IsLast { get; }
    public bool ShowSeparator => !IsLast;
}

/// <summary>Which column a pane is ordered by.</summary>
public enum FileSortColumn
{
    Name,
    Size,
    Modified
}

/// <summary>
/// One side of the dual-pane commander: breadcrumb, editable address bar with go-to-path,
/// listing with multi-select, and the friendly failure states (empty / access denied / error).
/// Owns no I/O — everything goes through <see cref="IFileTransferService"/>.
/// </summary>
public sealed partial class PaneViewModel : ObservableObject
{
    private readonly IFileTransferService _service;
    private string? _lastGoodPath;

    /// <summary>Everything the last listing returned; <see cref="Items"/> is the filtered+sorted view of it.</summary>
    private readonly List<FileEntryViewModel> _allItems = new();

    /// <summary>Path rules for this pane — Windows for local, POSIX for a remote SFTP host.</summary>
    public RdpManager.Application.Files.IPathModel PathModel { get; set; } = RdpManager.Application.Files.WindowsPathModel.Instance;

    public PaneViewModel(TransferEndpoint endpoint, IFileTransferService service, string title, string emptyText)
    {
        Endpoint = endpoint;
        _service = service;
        _title = title;
        EmptyText = emptyText;
    }

    public TransferEndpoint Endpoint { get; }
    public string EmptyText { get; }

    public ObservableCollection<FileEntryViewModel> Items { get; } = new();
    public ObservableCollection<BreadcrumbItemViewModel> Crumbs { get; } = new();

    [ObservableProperty] private string _title;
    [ObservableProperty] private string _currentPath = string.Empty;
    [ObservableProperty] private bool _isEditingPath;
    [ObservableProperty] private string _pathDraft = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _isAccessDenied;
    [ObservableProperty] private string _accessDeniedText = string.Empty;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorText = string.Empty;

    /// <summary>Live filter over the current directory only — never re-lists, never walks subfolders.</summary>
    [ObservableProperty] private string _filterText = string.Empty;

    /// <summary>True when the directory has rows but the filter matched none of them.</summary>
    [ObservableProperty] private bool _isFilterEmpty;

    [ObservableProperty] private FileSortColumn _sortColumn = FileSortColumn.Name;
    [ObservableProperty] private bool _sortDescending;

    /// <summary>Header arrows: only the active column shows one. "" up, "" down.</summary>
    public string NameSortGlyph => GlyphFor(FileSortColumn.Name);
    public string SizeSortGlyph => GlyphFor(FileSortColumn.Size);
    public string ModifiedSortGlyph => GlyphFor(FileSortColumn.Modified);

    private string GlyphFor(FileSortColumn column) =>
        SortColumn != column ? string.Empty : SortDescending ? "\uE70D" : "\uE70E";

    partial void OnFilterTextChanged(string value) => ApplyView();

    /// <summary>Click a header: same column flips direction, a new column starts ascending.</summary>
    [RelayCommand]
    public void SortBy(string column)
    {
        if (!Enum.TryParse<FileSortColumn>(column, ignoreCase: true, out var parsed)) return;

        if (SortColumn == parsed)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortColumn = parsed;
            SortDescending = false;
        }

        OnPropertyChanged(nameof(NameSortGlyph));
        OnPropertyChanged(nameof(SizeSortGlyph));
        OnPropertyChanged(nameof(ModifiedSortGlyph));
        ApplyView();
    }

    [RelayCommand]
    public void ClearFilter() => FilterText = string.Empty;

    /// <summary>
    /// Rebuilds <see cref="Items"/> from <see cref="_allItems"/>: filter first, then sort.
    /// Folders are not hoisted - every row competes on the sorted column alone.
    /// </summary>
    private void ApplyView()
    {
        var filter = FilterText?.Trim() ?? string.Empty;
        IEnumerable<FileEntryViewModel> query = _allItems;

        if (filter.Length > 0)
            query = query.Where(i => i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));

        // Folders and files rank together: a sort the user asked for should order every row by
        // the column they clicked, not park one kind of row above the other. Name breaks ties.
        var ordered = SortColumn switch
        {
            FileSortColumn.Size => SortDescending
                ? query.OrderByDescending(i => i.Entry.SizeBytes).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                : query.OrderBy(i => i.Entry.SizeBytes).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
            FileSortColumn.Modified => SortDescending
                ? query.OrderByDescending(i => i.Entry.ModifiedAt ?? DateTimeOffset.MinValue).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                : query.OrderBy(i => i.Entry.ModifiedAt ?? DateTimeOffset.MinValue).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
            _ => SortDescending
                ? query.OrderByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase)
                : query.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
        };

        Items.Clear();
        foreach (var item in ordered)
            Items.Add(item);

        IsEmpty = _allItems.Count == 0;
        IsFilterEmpty = _allItems.Count > 0 && Items.Count == 0;
        SetSelection(Array.Empty<FileEntryViewModel>());
    }

    /// <summary>Rows currently selected in the ListView (the page keeps this in sync).</summary>
    public IReadOnlyList<FileEntryViewModel> SelectedEntries { get; private set; } = Array.Empty<FileEntryViewModel>();

    /// <summary>Raised when the selection set changes, so the shell can light up Send/Get.</summary>
    public event Action? SelectionChanged;

    public void SetSelection(IEnumerable<FileEntryViewModel> entries)
    {
        SelectedEntries = entries.ToList();
        SelectionChanged?.Invoke();
    }

    [RelayCommand]
    public async Task NavigateAsync(string path)
    {
        var normalized = PathModel.Normalize(path);
        if (!normalized.IsSuccess)
        {
            HasError = true;
            ErrorText = normalized.Error!.Message;
            return;
        }

        var previousPath = CurrentPath;

        IsLoading = true;
        HasError = false;
        try
        {
            var result = await _service.ListDirectoryAsync(Endpoint, normalized.Value, CancellationToken.None);
            if (!result.IsSuccess)
            {
                if (result.Error!.Kind == ErrorKind.PermissionDenied)
                {
                    // Friendly pane state, not a crash — with a way back.
                    CurrentPath = normalized.Value;
                    UpdateCrumbs(normalized.Value);
                    _allItems.Clear();
                    Items.Clear();
                    IsFilterEmpty = false;
                    SetSelection(Array.Empty<FileEntryViewModel>());
                    IsAccessDenied = true;
                    AccessDeniedText = $"You don't have permission to read {PathModel.GetFileName(normalized.Value)}. " +
                                       "Connect with an account that can, or ask an admin.";
                    IsEmpty = false;
                    return;
                }
                HasError = true;
                ErrorText = result.Error.Message;
                return;
            }

            CurrentPath = normalized.Value;
            _lastGoodPath = normalized.Value;
            IsAccessDenied = false;
            IsEditingPath = false;
            UpdateCrumbs(normalized.Value);

            _allItems.Clear();
            foreach (var entry in result.Value)
                _allItems.Add(new FileEntryViewModel(entry));

            // A filter belongs to the folder it was typed in — landing somewhere new starts clean.
            if (!string.Equals(previousPath, normalized.Value, StringComparison.OrdinalIgnoreCase) && FilterText.Length > 0)
                FilterText = string.Empty;   // setter re-runs ApplyView
            else
                ApplyView();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public Task RefreshAsync() =>
        string.IsNullOrEmpty(CurrentPath) ? Task.CompletedTask : NavigateAsync(CurrentPath);

    /// <summary>The address bar's Go: accepts \ or /, resolves relative input against the current directory.</summary>
    [RelayCommand]
    public async Task GoToPathAsync()
    {
        var normalized = PathModel.Normalize(PathDraft, string.IsNullOrEmpty(CurrentPath) ? null : CurrentPath);
        if (!normalized.IsSuccess)
        {
            HasError = true;
            ErrorText = normalized.Error!.Message;
            return;
        }
        await NavigateAsync(normalized.Value);
    }

    [RelayCommand]
    public void BeginEditPath()
    {
        PathDraft = CurrentPath;
        IsEditingPath = true;
    }

    [RelayCommand]
    public void CancelEditPath() => IsEditingPath = false;

    [RelayCommand]
    public async Task UpAsync()
    {
        var parent = string.IsNullOrEmpty(CurrentPath) ? null : PathModel.GetParent(CurrentPath);
        if (parent is not null) await NavigateAsync(parent);
    }

    /// <summary>Escape hatch from the access-denied state.</summary>
    [RelayCommand]
    public async Task BackAsync()
    {
        var target = PathModel.GetParent(CurrentPath) ?? _lastGoodPath;
        if (target is not null) await NavigateAsync(target);
    }

    /// <summary>Double-click / Enter on a row: folders open, files are just selected.</summary>
    public async Task OpenAsync(FileEntryViewModel entry)
    {
        if (entry.IsFolder) await NavigateAsync(entry.Entry.FullPath);
    }

    public async Task<Error?> CreateFolderAsync(string name)
    {
        var result = await _service.CreateDirectoryAsync(Endpoint, CurrentPath, name, CancellationToken.None);
        if (!result.IsSuccess) return result.Error;
        await RefreshAsync();
        return null;
    }

    public async Task<Error?> RenameEntryAsync(FileEntryViewModel entry, string newName)
    {
        var result = await _service.RenameAsync(Endpoint, entry.Entry.FullPath, newName, CancellationToken.None);
        if (!result.IsSuccess) return result.Error;
        await RefreshAsync();
        return null;
    }

    public async Task<Error?> DeleteEntryAsync(FileEntryViewModel entry)
    {
        var result = await _service.DeleteAsync(Endpoint, entry.Entry.FullPath, CancellationToken.None);
        if (!result.IsSuccess) return result.Error;
        await RefreshAsync();
        return null;
    }

    private void UpdateCrumbs(string path)
    {
        Crumbs.Clear();
        var segments = PathModel.Breadcrumbs(path);
        for (var i = 0; i < segments.Count; i++)
            Crumbs.Add(new BreadcrumbItemViewModel(segments[i], i == segments.Count - 1));
    }
}
