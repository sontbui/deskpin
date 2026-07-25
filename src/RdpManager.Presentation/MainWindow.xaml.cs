using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RdpManager.Presentation.Services;
using RdpManager.Presentation.Views;

namespace RdpManager.Presentation;

/// <summary>
/// The single application window. Code-behind is intentionally thin: it wires the custom title bar,
/// routes navigation to pages, and surfaces toasts. All decisions live in ViewModels.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly IDialogService _dialogs;

    public MainWindow(IServiceProvider services, IDialogService dialogs)
    {
        _services = services;
        _dialogs = dialogs;
        InitializeComponent();

        Title = "Deskpin";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Compact, launcher-style window — no wasted space.
        AppWindow?.Resize(new Windows.Graphics.SizeInt32(480, 800));
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "deskpin.ico");
        if (System.IO.File.Exists(iconPath)) AppWindow?.SetIcon(iconPath);

        if (_services.GetService<IToastService>() is ToastService toasts)
            toasts.ToastRequested += (_, e) => ShowToast(e.Message, e.Undo);

        // Do the first navigation once the visual tree is ready, not inside the constructor,
        // to avoid re-entering XAML before the Frame is live.
        Root.Loaded += (_, _) =>
        {
            _dialogs.XamlRoot = Content?.XamlRoot;
            if (ContentFrame.CurrentSourcePageType is null)
                ContentFrame.Navigate(typeof(MachinesPage));
        };
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (ContentFrame is null) return;

        Type? target;
        if (args.IsSettingsSelected)
        {
            target = typeof(SettingsPage);
        }
        else
        {
            var tag = (args.SelectedItemContainer as NavigationViewItem)?.Tag as string;
            target = tag switch
            {
                "Machines" => typeof(MachinesPage),
                "Groups" => typeof(GroupsPage),
                "Activity" => typeof(ActivityPage),
                _ => null,
            };
        }

        if (target is null || ContentFrame.CurrentSourcePageType == target) return;
        _dialogs.XamlRoot = Content?.XamlRoot;
        ContentFrame.Navigate(target);
    }

    private void ShowToast(string message, Action? undo)
    {
        Toast.Message = message;
        Toast.IsOpen = true;
    }
}
