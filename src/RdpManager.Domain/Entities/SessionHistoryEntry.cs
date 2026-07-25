using RdpManager.Domain.Enums;

namespace RdpManager.Domain.Entities;

/// <summary>An immutable audit record: one thing that happened to one machine.</summary>
public sealed class SessionHistoryEntry
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid MachineId { get; private set; }
    public HistoryEventType Type { get; private set; }
    public string? Detail { get; private set; }
    public MatchConfidence? Confidence { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    private SessionHistoryEntry() { } // EF

    public SessionHistoryEntry(
        Guid machineId,
        HistoryEventType type,
        DateTimeOffset occurredAt,
        string? detail = null,
        MatchConfidence? confidence = null)
    {
        MachineId = machineId;
        Type = type;
        OccurredAt = occurredAt;
        Detail = detail;
        Confidence = confidence;
    }
}
