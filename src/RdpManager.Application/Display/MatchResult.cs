using RdpManager.Domain.Entities;
using RdpManager.Domain.ValueObjects;

namespace RdpManager.Application.Display;

/// <summary>One saved monitor paired with the live monitor it was matched to.</summary>
public sealed record MonitorMapping(
    MonitorFingerprint Saved,
    MonitorInfo? Matched,
    double Score)
{
    public bool IsMatched => Matched is not null;
}

/// <summary>
/// The outcome of matching a saved <see cref="DisplayProfile"/> against the live topology.
/// <see cref="ResolvedMonitorIds"/> is what goes into the RDP <c>selectedmonitors</c> field.
/// </summary>
public sealed class MatchResult
{
    public IReadOnlyList<MonitorMapping> Mappings { get; }
    public ConfidenceScore Confidence { get; }

    public MatchResult(IReadOnlyList<MonitorMapping> mappings, ConfidenceScore confidence)
    {
        Mappings = mappings;
        Confidence = confidence;
    }

    /// <summary>The live mstsc ids for every saved monitor that matched, in saved order.</summary>
    public IReadOnlyList<int> ResolvedMonitorIds =>
        Mappings.Where(m => m.IsMatched)
                .OrderBy(m => m.Saved.OrdinalInProfile)
                .Select(m => m.Matched!.MstscMonitorId)
                .ToList();

    public bool AllMatched => Mappings.All(m => m.IsMatched);
    public int MatchedCount => Mappings.Count(m => m.IsMatched);
}
