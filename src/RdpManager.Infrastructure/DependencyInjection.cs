using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RdpManager.Application.Abstractions;
using RdpManager.Application.Display;
using RdpManager.Application.History;
using RdpManager.Application.Machines;
using RdpManager.Application.Rdp;
using RdpManager.Application.Sessions;
using RdpManager.Application.Settings;
using RdpManager.Domain.Abstractions;
using RdpManager.Application.Files;
using RdpManager.Infrastructure.Credentials;
using RdpManager.Infrastructure.Display;
using RdpManager.Infrastructure.Files;
using RdpManager.Infrastructure.Launching;
using RdpManager.Infrastructure.Persistence;
using RdpManager.Infrastructure.Persistence.Repositories;
using RdpManager.Infrastructure.Reachability;
using RdpManager.Infrastructure.Rdp;

namespace RdpManager.Infrastructure;

/// <summary>Composition root helpers. The app calls AddApplication + AddInfrastructure at startup.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Pure, stateless services — safe as singletons.
        services.AddSingleton<DisplayMatcher>();
        services.AddSingleton<IRdpProfileBuilder, RdpProfileBuilder>();

        // Transient, not Scoped: a WinUI app has no request scope, and these are consumed by
        // singleton ViewModels — a singleton capturing a scoped service would fail to resolve.
        services.AddTransient<MachineService>();
        services.AddTransient<HistoryService>();
        services.AddTransient<SettingsService>();
        services.AddTransient<RemoteSessionUseCase>();

        // Singleton on purpose: the transfer queue is shared app-wide state the dock observes.
        services.AddSingleton<FileTransferUseCase>();
        services.AddSingleton<IFileTransferService>(sp => sp.GetRequiredService<FileTransferUseCase>());
        return services;
    }

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string dbPath)
    {
        services.AddSingleton<IClock, SystemClock>();

        services.AddDbContextFactory<AppDbContext>(o =>
            o.UseSqlite($"Data Source={dbPath};Cache=Shared"));

        services.AddSingleton<IMachineRepository, MachineRepository>();
        services.AddSingleton<IHistoryRepository, HistoryRepository>();
        services.AddSingleton<ISettingsStore, SettingsStore>();

        services.AddSingleton<IEdidReader, NullEdidReader>();
        services.AddSingleton<IDisplayTopologyProvider, Win32DisplayTopologyProvider>();
        services.AddSingleton<ICredentialStore, DpapiCredentialStore>();
        services.AddSingleton<ICredentialInjector, CredentialManagerInjector>();
        services.AddSingleton<IReachabilityProbe, TcpReachabilityProbe>();

        services.AddSingleton<TempRdpFileWriter>();
        services.AddSingleton<RdpSigner>();
        services.AddSingleton<IRemoteLauncher, MstscRemoteLauncher>();
        services.AddSingleton<IUninstallCleanup, Cleanup.UninstallCleanup>();

        // Files console (SFTP / WinSCP-style): local over System.IO, remote over SFTP.
        services.AddSingleton<ILocalFileSystem, LocalFileSystem>();
        services.AddSingleton<IRemoteFileSystem, SftpRemoteFileSystem>();
        services.AddSingleton<IRemoteConnectionFactory, RemoteConnectionFactory>();
        services.AddSingleton<IRemoteAdminSetup, RemoteAdminSetup>();
        // Drive redirection stays a real RDP setting (the Remote button), just not tied to Files.
        services.AddSingleton<IDriveRedirectionSettings, DriveRedirectionSettingsStore>();
        return services;
    }

    /// <summary>
    /// Ensures the database schema exists at startup. We use EnsureCreated (schema built directly
    /// from the model) because the app ships without a migration history. When you later want
    /// versioned schema evolution, add an EF migration and switch this to <c>MigrateAsync</c>.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider provider, CancellationToken ct = default)
    {
        var factory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.EnsureCreatedAsync(ct);

        // Lightweight in-place migration: EnsureCreated never alters an existing schema, so
        // databases created before Machine.Os existed need the column added by hand.
        var hasOs = false;
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(ct);
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('Machines');";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                if (string.Equals(reader.GetString(1), "Os", StringComparison.OrdinalIgnoreCase))
                    hasOs = true;
        }
        if (!hasOs)
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Machines ADD COLUMN \"Os\" INTEGER NOT NULL DEFAULT 0;", ct);

        var hasSshPort = false;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('Machines');";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                if (string.Equals(reader.GetString(1), "SshPort", StringComparison.OrdinalIgnoreCase))
                    hasSshPort = true;
        }
        if (!hasSshPort)
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Machines ADD COLUMN \"SshPort\" INTEGER NOT NULL DEFAULT 22;", ct);
    }
}
