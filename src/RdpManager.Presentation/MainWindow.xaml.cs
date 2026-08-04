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

        SizeAndCenter(720, 760); // balanced default, scaled for the monitor's DPI
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

        var tag = (args.SelectedItemContainer as NavigationViewItem)?.Tag as string;
        Type? target = tag switch
        {
            "Machines" => typeof(MachinesPage),
            "Activity" => typeof(ActivityPage),
            "Info" => typeof(SettingsPage),
            _ => null,
        };

        if (target is null || ContentFrame.CurrentSourcePageType == target) return;
        _dialogs.XamlRoot = Content?.XamlRoot;
        ContentFrame.Navigate(target);
    }

    private void ShowToast(string message, Action? undo)
    {
        Toast.Message = message;
        Toast.IsOpen = true;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    // Sizes the window in DPI-independent "effective" pixels and centers it on the current monitor.
    private void SizeAndCenter(int effectiveWidth, int effectiveHeight)
    {
        if (AppWindow is null) return;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dpi = GetDpiForWindow(hwnd);
        var scale = dpi <= 0 ? 1.0 : dpi / 96.0;

        var w = (int)(effectiveWidth * scale);
        var h = (int)(effectiveHeight * scale);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));

        var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
            AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
        if (area is not null)
        {
            var x = area.WorkArea.X + ((area.WorkArea.Width - w) / 2);
            var y = area.WorkArea.Y + ((area.WorkArea.Height - h) / 2);
            AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
        }
    }
}
