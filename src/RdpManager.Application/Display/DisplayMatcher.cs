using RdpManager.Domain.Entities;
using RdpManager.Domain.ValueObjects;

namespace RdpManager.Application.Display;

/// <summary>
/// Pure, dependency-free algorithm that maps a saved <see cref="DisplayProfile"/> onto the
/// live <see cref="DisplayTopology"/>, tolerating monitor <em>index</em> changes.
///
/// Strategy: identity-first, geometry-second. Each (saved, live) pair is scored in [0,1];
/// pairs are assigned greedily by descending score with uniqueness (deterministic for the
/// small monitor counts we deal with). Overall confidence divides the matched score mass by
/// the number of saved monitors, so dropping monitors (e.g. 3 → 1) correctly collapses to Low.
///
/// This class has NO I/O and NO framework dependencies — it is unit-tested exhaustively.
/// </summary>
public sealed class DisplayMatcher
{
    // Identity is POSITIVE evidence of sameness; it boosts a geometry match rather than gating it.
    // When we have no positive identity signal we fall back to geometry alone, so a benign replug
    // (different device path, no EDID) is not punished. Only a *contradiction* — two different,
    // non-empty EDID serials — is treated as strong evidence of a different monitor.
    private const double IdentityWeight = 0.60;
    private const double GeometryWeight = 0.40;

    private const double ContradictionCap = 0.35;     // different serials → cap the score low
    private const double PositionTolerancePx = 120.0; // a monitor that moved less than this is "same spot"
    private const double MinAcceptableScore = 0.20;   // below this we treat a pair as "not a match"

    public MatchResult Match(DisplayProfile profile, DisplayTopology topology)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(topology);

        var saved = profile.SelectedMonitors;

        // Score every candidate pair.
        var candidates = new List<(int savedIdx, int liveIdx, double score)>();
        for (var s = 0; s < saved.Count; s++)
            for (var l = 0; l < topology.Monitors.Count; l++)
                candidates.Add((s, l, ScorePair(saved[s], topology.Monitors[l])));

        // Greedy uniqueness assignment. Ties broken deterministically by (savedIdx, liveIdx).
        candidates.Sort((a, b) =>
        {
            var c = b.score.CompareTo(a.score);
            if (c != 0) return c;
            c = a.savedIdx.CompareTo(b.savedIdx);
            return c != 0 ? c : a.liveIdx.CompareTo(b.liveIdx);
        });

        var savedTaken = new bool[saved.Count];
        var liveTaken = new bool[topology.Monitors.Count];
        var matchedLiveFor = new int?[saved.Count];
        var scoreFor = new double[saved.Count];

        foreach (var (si, li, score) in candidates)
        {
            if (savedTaken[si] || liveTaken[li]) continue;
            if (score < MinAcceptableScore) continue;
            savedTaken[si] = true;
            liveTaken[li] = true;
            matchedLiveFor[si] = li;
            scoreFor[si] = score;
        }

        var mappings = new List<MonitorMapping>(saved.Count);
        double scoreMass = 0;
        for (var s = 0; s < saved.Count; s++)
        {
            var live = matchedLiveFor[s] is int li ? topology.Monitors[li] : null;
            mappings.Add(new MonitorMapping(saved[s], live, scoreFor[s]));
            scoreMass += scoreFor[s];
        }

        // Confidence = matched score mass / saved count. Unmatched saved monitors count as 0.
        var confidence = ConfidenceScore.Of(saved.Count == 0 ? 0 : scoreMass / saved.Count);
        return new MatchResult(mappings, confidence);
    }

    private static double ScorePair(IMonitorIdentity saved, IMonitorIdentity live)
    {
        var geometry = GeometryScore(saved.Geometry, live.Geometry, saved.IsPrimary, live.IsPrimary);

        // Two different, non-empty serials => almost certainly a different physical monitor.
        var serialsContradict = !string.IsNullOrEmpty(saved.EdidSerial)
                                && !string.IsNullOrEmpty(live.EdidSerial)
                                && !NonEmptyEqual(saved.EdidSerial, live.EdidSerial);
        if (serialsContradict)
            return Math.Min(geometry, ContradictionCap);

        // Positive identity evidence, strongest first.
        double? identity = PositiveIdentity(saved, live);

        // No positive identity signal (e.g. benign replug, EDID hidden): trust geometry alone.
        if (identity is null)
            return geometry;

        return (IdentityWeight * identity.Value) + (GeometryWeight * geometry);
    }

    /// <summary>Positive evidence that two descriptors are the same monitor, or null if unknown.</summary>
    private static double? PositiveIdentity(IMonitorIdentity a, IMonitorIdentity b)
    {
        if (NonEmptyEqual(a.EdidSerial, b.EdidSerial)) return 1.0;      // gold: same serial
        if (NonEmptyEqual(a.DevicePath, b.DevicePath)) return 0.9;      // same physical port
        if (NonEmptyEqual(a.EdidManufacturer, b.EdidManufacturer)
            && NonEmptyEqual(a.EdidProductCode, b.EdidProductCode)) return 0.6; // same make+model
        return null;
    }

    private static double GeometryScore(MonitorGeometry a, MonitorGeometry b, bool aPrimary, bool bPrimary)
    {
        var res = a.SameResolution(b) ? 1.0 : ResolutionCloseness(a, b);
        var dist = a.TopLeftDistanceTo(b);
        var pos = dist <= PositionTolerancePx ? 1.0 : Math.Max(0.0, 1.0 - ((dist - PositionTolerancePx) / 4000.0));
        var primary = aPrimary == bPrimary ? 1.0 : 0.0;
        return (0.50 * res) + (0.40 * pos) + (0.10 * primary);
    }

    private static double ResolutionCloseness(MonitorGeometry a, MonitorGeometry b)
    {
        double maxArea = Math.Max(a.Area, b.Area);
        if (maxArea <= 0) return 0;
        double diff = Math.Abs(a.Area - b.Area) / maxArea;
        return Math.Max(0.0, 1.0 - diff);
    }

    private static bool NonEmptyEqual(string? x, string? y) =>
        !string.IsNullOrEmpty(x) && !string.IsNullOrEmpty(y)
        && string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
}
