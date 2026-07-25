using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace RdpManager.Presentation.Services;

/// <summary>Marshals work onto the UI thread so ViewModels never touch DispatcherQueue directly.</summary>
public interface IDispatcherService
{
    void Post(Action action);
    Task RunAsync(Func<Task> action);
}

public sealed class DispatcherService : IDispatcherService
{
    private readonly DispatcherQueue _queue = DispatcherQueue.GetForCurrentThread();

    public void Post(Action action) => _queue.TryEnqueue(() => action());

    public Task RunAsync(Func<Task> action)
    {
        var tcs = new TaskCompletionSource();
        _queue.TryEnqueue(async () =>
        {
            try { await action(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }
}

public enum DialogChoice { Primary, Secondary, Cancel }

public interface IDialogService
{
    Task<bool> ConfirmDeleteAsync(string machineName);
    Task<DialogChoice> LayoutChangedAsync(string message);
    Task ErrorAsync(string title, string message);
    Task<bool> ConfirmAsync(string title, string message, string primaryText, string closeText);
    XamlRoot? XamlRoot { get; set; }
}

public sealed class DialogService : IDialogService
{
    public XamlRoot? XamlRoot { get; set; }

    public async Task<bool> ConfirmDeleteAsync(string machineName)
    {
        var dialog = new ContentDialog
        {
            Title = $"Delete “{machineName}”?",
            Content = "This removes the profile and its saved display positions. The Credential Manager entry is left untouched. This can't be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task<DialogChoice> LayoutChangedAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "Your monitor layout changed",
            Content = message,
            PrimaryButtonText = "Open on this screen",
            SecondaryButtonText = "Reconfigure…",
            CloseButtonText = "Cancel launch",
            XamlRoot = XamlRoot,
        };
        return (await dialog.ShowAsync()) switch
        {
            ContentDialogResult.Primary => DialogChoice.Primary,
            ContentDialogResult.Secondary => DialogChoice.Secondary,
            _ => DialogChoice.Cancel,
        };
    }

    public async Task ErrorAsync(string title, string message)
    {
        await new ContentDialog { Title = title, Content = message, CloseButtonText = "OK", XamlRoot = XamlRoot }.ShowAsync();
    }

    public async Task<bool> ConfirmAsync(string title, string message, string primaryText, string closeText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = primaryText,
            CloseButtonText = closeText,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}

public interface IToastService
{
    void Show(string message, Action? undo = null);
}

/// <summary>Non-blocking toast. Bound to an InfoBar in the shell; kept behind an interface so VMs stay UI-free.</summary>
public sealed class ToastService : IToastService
{
    public event EventHandler<ToastEventArgs>? ToastRequested;
    public void Show(string message, Action? undo = null) => ToastRequested?.Invoke(this, new ToastEventArgs(message, undo));
}

public sealed class ToastEventArgs(string message, Action? undo) : EventArgs
{
    public string Message { get; } = message;
    public Action? Undo { get; } = undo;
}
