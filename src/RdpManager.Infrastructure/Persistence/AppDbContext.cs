using Microsoft.EntityFrameworkCore;
using RdpManager.Domain.Entities;

namespace RdpManager.Infrastructure.Persistence;

/// <summary>
/// EF Core context. Note: constructed via <c>IDbContextFactory</c> in the app because WinUI has
/// no per-request scope. All entity mapping lives in configuration classes so Domain stays clean.
/// </summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<DisplayProfile> DisplayProfiles => Set<DisplayProfile>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<SessionHistoryEntry> History => Set<SessionHistoryEntry>();
    public DbSet<ProtectedSecret> Secrets => Set<ProtectedSecret>();
    public DbSet<SettingEntry> Settings => Set<SettingEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

/// <summary>Persistence-only record for a DPAPI-protected secret blob (never mapped into Domain).</summary>
public sealed class ProtectedSecret
{
    public string Reference { get; set; } = Guid.NewGuid().ToString("N");
    public byte[] ProtectedBlob { get; set; } = Array.Empty<byte>();
    public byte[] Entropy { get; set; } = Array.Empty<byte>();
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SettingEntry
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
