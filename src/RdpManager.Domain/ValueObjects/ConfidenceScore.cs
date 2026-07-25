using RdpManager.Domain.Enums;

namespace RdpManager.Domain.ValueObjects;

/// <summary>
/// A normalized match confidence in [0, 1] with its banded interpretation.
/// Thresholds live here so the whole system agrees on what "high confidence" means.
/// </summary>
public sealed record ConfidenceScore
{
    public const double MediumThreshold = 0.50;
    public const double HighThreshold = 0.85;

    public double Value { get; }

    private ConfidenceScore(double value) => Value = value;

    public static ConfidenceScore Of(double value)
    {
        if (double.IsNaN(value))
            throw new ArgumentException("Confidence must be a number.", nameof(value));
        return new ConfidenceScore(Math.Clamp(value, 0.0, 1.0));
    }

    public MatchConfidence Band => Value >= HighThreshold
        ? MatchConfidence.High
        : Value >= MediumThreshold
            ? MatchConfidence.Medium
            : MatchConfidence.Low;

    public override string ToString() => $"{Value:P0} ({Band})";
}
