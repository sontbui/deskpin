using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using RdpManager.Infrastructure;
using RdpManager.Presentation.Services;
using RdpManager.Presentation.ViewModels;

namespace RdpManager.Presentation;

/// <summary>
/// Composition root. The Generic Host owns DI + logging; the whole app is resolved from it, so
/// there is no static mutable state and every ViewModel gets its dependencies injected.
/// </summary>
public partial class App : Microsoft.UI.Xaml.Application
{
    public static IHost Host { get; private set; } = null!;
    private Window? _window;

    public App()
    {
        InitializeComponent();
        // Surface any fatal startup/UI-thread error instead of a cryptic ExecutionEngineException.
        UnhandledException += (_, e) => { LogFatal(e.Exception); e.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogFatal(e.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) => LogFatal(e.Exception);
    }

    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RdpManager", "startup-error.log");

    private static void LogFatal(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:o}]\n{ex}\n\n");
        }
        catch { /* nothing else we can do */ }
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
      try
      {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RdpManager", "rdpmanager.db");

        // Uninstall hook: `Deskpin.exe --cleanup` removes cert, secrets, registry and data, no UI.
        if (Environment.GetCommandLineArgs().Any(a => string.Equals(a, "--cleanup", StringComparison.OrdinalIgnoreCase)))
        {
            await RunUninstallCleanupAsync(dbPath);
            Environment.Exit(0);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddApplication();
                services.AddInfrastructure(dbPath);

                // Presentation services
                services.AddSingleton<IDispatcherService, DispatcherService>();
                services.AddSingleton<IDialogService, DialogService>();
                services.AddSingleton<IToastService, ToastService>();
                services.AddSingleton<GitHubReleaseService>();
                services.AddSingleton<ReleaseNotesReader>();
                services.AddSingleton<UpdateChecker>();

                // ViewModels
                services.AddSingleton<ShellViewModel>();
                services.AddSingleton<MachinesViewModel>();
                services.AddTransient<MachineDetailViewModel>();
                services.AddTransient<MonitorPickerViewModel>();
                services.AddTransient<CommandPaletteViewModel>();
                services.AddTransient<ActivityViewModel>();
                // Factory so the master VM can spin up a fresh picker per configure request.
                services.AddSingleton<Func<MonitorPickerViewModel>>(sp => sp.GetRequiredService<MonitorPickerViewModel>);

                services.AddSingleton<MainWindow>();
            })
            .Build();

        await Host.Services.InitializeDatabaseAsync();

        _window = Host.Services.GetRequiredService<MainWindow>();
        _window.Activate();

        // Non-blocking: offer an update a few seconds after the UI is up.
        _ = CheckForUpdatesAsync();
      }
      catch (Exception ex)
      {
        LogFatal(ex);
        throw;
      }
    }

    private static async Task CheckForUpdatesAsync()
    {
        await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(3));
        await Host.Services.GetRequiredService<UpdateChecker>().CheckAsync();
    }

    private static async Task RunUninstallCleanupAsync(string dbPath)
    {
        try
        {
            using var host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddApplication();
                    services.AddInfrastructure(dbPath);
                })
                .Build();
            await host.Services.GetRequiredService<RdpManager.Application.Abstractions.IUninstallCleanup>()
                .RunAsync(System.Threading.CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogFatal(ex);
        }
    }
}
