using Microsoft.EntityFrameworkCore;
using RdpManager.Application.Abstractions;
using RdpManager.Domain.Entities;

namespace RdpManager.Infrastructure.Persistence.Repositories;

public sealed class HistoryRepository : IHistoryRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    public HistoryRepository(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task AddAsync(SessionHistoryEntry entry, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.History.Add(entry);
        await db.SaveChangesAsync(ct);
    }

    // NOTE: SQLite can't translate ORDER BY on DateTimeOffset, so we order on the client.
    // History is small (per-user), so loading then sorting in memory is fine.
    public async Task<IReadOnlyList<SessionHistoryEntry>> ListForMachineAsync(Guid machineId, int take, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.History.AsNoTracking()
            .Where(h => h.MachineId == machineId)
            .ToListAsync(ct);
        return rows.OrderByDescending(h => h.OccurredAt).Take(take).ToList();
    }

    public async Task<IReadOnlyList<SessionHistoryEntry>> ListRecentAsync(int take, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.History.AsNoTracking().ToListAsync(ct);
        return rows.OrderByDescending(h => h.OccurredAt).Take(take).ToList();
    }
}

public sealed class SettingsStore : ISettingsStore
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    public SettingsStore(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Settings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(ct);
    }

    public async Task SetAsync(string key, string value, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (existing is null) db.Settings.Add(new SettingEntry { Key = key, Value = value });
        else existing.Value = value;
        await db.SaveChangesAsync(ct);
    }
}
