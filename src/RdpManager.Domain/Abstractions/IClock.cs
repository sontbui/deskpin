namespace RdpManager.Domain.Abstractions;

/// <summary>Abstracts "now" so time-dependent logic is testable. No static DateTime.Now anywhere.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
