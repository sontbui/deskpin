namespace RdpManager.Domain.Entities;

/// <summary>
/// The set of monitors a user chose for a machine, captured as fingerprints.
/// One profile per machine. Contains no indexes — only durable identities.
/// </summary>
public sealed class DisplayProfile
{
    private readonly List<MonitorFingerprint> _selectedMonitors = new();

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid MachineId { get; private set; }
    public DateTimeOffset ConfiguredAt { get; private set; }

    public IReadOnlyList<MonitorFingerprint> SelectedMonitors => _selectedMonitors;

    private DisplayProfile() { } // EF

    public DisplayProfile(Guid machineId, IEnumerable<MonitorFingerprint> monitors, DateTimeOffset configuredAt)
    {
        var list = monitors?.ToList() ?? throw new ArgumentNullException(nameof(monitors));
        if (list.Count == 0)
            throw new ArgumentException("A display profile needs at least one monitor.", nameof(monitors));

        MachineId = machineId;
        ConfiguredAt = configuredAt;
        _selectedMonitors.AddRange(list);
    }

    public bool IsMultiMonitor => _selectedMonitors.Count > 1;
}
