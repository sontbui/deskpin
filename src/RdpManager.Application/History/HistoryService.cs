using RdpManager.Application.Abstractions;
using RdpManager.Domain.Entities;

namespace RdpManager.Application.History;

public sealed class HistoryService
{
    private readonly IHistoryRepository _repo;
    public HistoryService(IHistoryRepository repo) => _repo = repo;

    public Task<IReadOnlyList<SessionHistoryEntry>> ForMachineAsync(Guid machineId, int take, CancellationToken ct)
        => _repo.ListForMachineAsync(machineId, take, ct);

    public Task<IReadOnlyList<SessionHistoryEntry>> RecentAsync(int take, CancellationToken ct)
        => _repo.ListRecentAsync(take, ct);
}
