using RdpManager.Domain.Entities;

namespace RdpManager.Application.Abstractions;

public interface IMachineRepository
{
    Task<Machine?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Machine>> ListAsync(CancellationToken ct);
    Task AddAsync(Machine machine, CancellationToken ct);
    Task UpdateAsync(Machine machine, CancellationToken ct);
    /// <summary>Lightweight single-column update — avoids reloading/rewriting the whole aggregate.</summary>
    Task MarkConnectedAsync(Guid id, DateTimeOffset when, CancellationToken ct);
    /// <summary>Replaces a machine's display profile: deletes the old one, then inserts the new one.</summary>
    Task SaveDisplayProfileAsync(Guid machineId, IReadOnlyList<MonitorFingerprint> monitors, DateTimeOffset when, CancellationToken ct);
    Task RemoveAsync(Guid id, CancellationToken ct);
}

public interface IHistoryRepository
{
    Task AddAsync(SessionHistoryEntry entry, CancellationToken ct);
    Task<IReadOnlyList<SessionHistoryEntry>> ListForMachineAsync(Guid machineId, int take, CancellationToken ct);
    Task<IReadOnlyList<SessionHistoryEntry>> ListRecentAsync(int take, CancellationToken ct);
}

public interface ISettingsStore
{
    Task<string?> GetAsync(string key, CancellationToken ct);
    Task SetAsync(string key, string value, CancellationToken ct);
}

/// <summary>Commits a unit of work. Repositories enlist; the service decides when to save.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
