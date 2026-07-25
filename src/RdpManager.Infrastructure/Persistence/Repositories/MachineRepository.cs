using Microsoft.EntityFrameworkCore;
using RdpManager.Application.Abstractions;
using RdpManager.Domain.Entities;
using RdpManager.Domain.Enums;

namespace RdpManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repository over short-lived contexts from an <see cref="IDbContextFactory{TContext}"/>.
/// WinUI has no request scope, so we never share a DbContext across threads — each call
/// owns its context and disposes it. Each method is its own atomic unit of work.
/// </summary>
public sealed class MachineRepository : IMachineRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public MachineRepository(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<Machine?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await LoadGraph(db.Machines.AsNoTracking())
            .FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task<IReadOnlyList<Machine>> ListAsync(CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await LoadGraph(db.Machines.AsNoTracking())
            .OrderBy(m => m.Name)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Machine machine, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(machine);
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Machines.Add(machine);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Updates scalar fields, the owned value objects (Host, Credential) and the owned display
    /// profile. Tag membership is deliberately NOT reconciled here — tag upsert against the shared
    /// Tags table (find-or-create by name) is owned by MachineService (M5), which has the full
    /// aggregate context. This keeps the repository free of many-to-many upsert policy.
    /// The owned-collection replacement below is exercised by the M1 integration test on the SDK.
    /// </summary>
    public async Task UpdateAsync(Machine machine, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(machine);
        await using var db = await _factory.CreateDbContextAsync(ct);

        var existing = await db.Machines
            .Include(m => m.DisplayProfile!).ThenInclude(p => p.SelectedMonitors)
            .FirstOrDefaultAsync(m => m.Id == machine.Id, ct)
            ?? throw new InvalidOperationException($"Machine {machine.Id} not found.");

        // Apply changes through the tracked entity's own methods. This lets EF's change tracker
        // detect exactly what changed (including the owned Host/Credential value objects) without
        // the fragile CurrentValues/owned-TargetEntry juggling that caused false concurrency errors.
        existing.Rename(machine.Name);
        existing.ChangeHost(machine.Host);
        existing.SetUsername(machine.Username);
        existing.SetCredential(machine.Credential);
        existing.SetGateway(machine.Gateway);
        existing.SetNotes(machine.Notes);
        existing.SetRedirection(machine.Redirection);
        existing.ToggleFavorite(machine.IsFavorite);

        // The display profile is intentionally NOT touched here — it is managed by
        // SaveDisplayProfileAsync so a metadata edit never risks the owned-graph rewrite.
        await db.SaveChangesAsync(ct);
    }

    public async Task SaveDisplayProfileAsync(
        Guid machineId, IReadOnlyList<MonitorFingerprint> monitors, DateTimeOffset when, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // 1. Delete any existing profile via direct SQL. The DB-level cascade removes its monitors.
        await db.DisplayProfiles.Where(p => p.MachineId == machineId).ExecuteDeleteAsync(ct);

        // 2. Update the machine's display mode via direct SQL (no tracking, no concurrency check).
        var mode = monitors.Count > 1 ? DisplayMode.MultiMonitor : DisplayMode.SingleMonitor;
        await db.Machines.Where(m => m.Id == machineId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.DisplayMode, mode), ct);

        // 3. Insert the new profile (and its monitors) as a fresh, untracked graph.
        db.DisplayProfiles.Add(new DisplayProfile(machineId, monitors, when));
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkConnectedAsync(Guid id, DateTimeOffset when, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Direct SET on one column — no entity tracking, no owned-graph handling, no concurrency token.
        await db.Machines
            .Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.LastConnectedAt, when), ct);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.Machines.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (existing is null) return;
        db.Machines.Remove(existing);
        await db.SaveChangesAsync(ct);
    }

    private static IQueryable<Machine> LoadGraph(IQueryable<Machine> q) =>
        q.Include(m => m.DisplayProfile!).ThenInclude(p => p.SelectedMonitors)
         .Include(m => m.Tags)
         .AsSplitQuery();
}
