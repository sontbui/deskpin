using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RdpManager.Application.Files;
using RdpManager.Application.Rdp;

namespace RdpManager.Presentation.Views;

/// <summary>One local drive row in the redirection dialog.</summary>
public sealed partial class DriveToggleViewModel : ObservableObject
{
    public DriveToggleViewModel(string drive, bool isOn)
    {
        Drive = drive;
        Label = $"{drive} — local drive";
        _isOn = isOn;
    }

    public string Drive { get; }
    public string Label { get; }
    [ObservableProperty] private bool _isOn;
}

/// <summary>
/// Governs which local drives the remote session can see. Maps 1:1 to the
/// <c>drivestoredirect:s:</c> value via <see cref="DriveRedirection"/>. Turning everything off
/// is allowed — and honestly disables the Files console.
/// </summary>
public sealed partial class DriveRedirectionDialog : ContentDialog
{
    private readonly ObservableCollection<DriveToggleViewModel> _drives = new();

    public DriveRedirectionDialog(DriveRedirection current, IReadOnlyList<FileSystemEntry> localRoots)
    {
        InitializeComponent();

        foreach (var root in localRoots)
        {
            var drive = root.Name.TrimEnd('\\'); // "C:" from "C:" or "C:\"
            var isOn = current.RedirectAll || current.Drives.Contains(drive, System.StringComparer.OrdinalIgnoreCase);
            _drives.Add(new DriveToggleViewModel(drive, isOn));
        }

        DriveList.ItemsSource = _drives;
        AllDrivesSwitch.IsOn = current.RedirectAll;
        DynamicSwitch.IsOn = current.IncludeDynamicDrives;
        DriveList.IsEnabled = !current.RedirectAll;
    }

    /// <summary>The choice the user saved. Read after ShowAsync returns Primary.</summary>
    public DriveRedirection Result =>
        AllDrivesSwitch.IsOn
            ? DriveRedirection.All
            : DriveRedirection.Of(_drives.Where(d => d.IsOn).Select(d => d.Drive), DynamicSwitch.IsOn);

    private void OnAllDrivesToggled(object sender, RoutedEventArgs e)
    {
        DriveList.IsEnabled = !AllDrivesSwitch.IsOn;
        if (AllDrivesSwitch.IsOn)
            foreach (var drive in _drives) drive.IsOn = true;
    }
}
