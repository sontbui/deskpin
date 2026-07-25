using Microsoft.EntityFrameworkCore;
using RdpManager.Domain.Entities;
using RdpManager.Domain.Enums;
using RdpManager.Domain.ValueObjects;
using RdpManager.Infrastructure.Persistence.Repositories;
using Xunit;

namespace RdpManager.Infrastructure.Tests;

public sealed class MachineRepositoryTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();
    private readonly MachineRepository _repo;
    private readonly CancellationToken _ct = CancellationToken.None;

    public MachineRepositoryTests() => _repo = new MachineRepository(_factory);

    private static Machine BuildConfigured()
    {
        var m = new Machine("build-agent-01", HostAddress.Create("10.0.4.21"), DateTimeOffset.UnixEpoch, "svc_build");
        m.AddTag(new Tag("CI"));
        m.SetCredential(CredentialRef.Dpapi("ref-123"));
        var profile = new DisplayProfile(m.Id, new[]
        {
            new MonitorFingerprint(0, new MonitorGeometry(0, 0, 2560, 1440, Orientation.Landscape), "\\\\.\\D1", true, "DEL", "U27", "SN-CENTER"),
            new MonitorFingerprint(1, new MonitorGeometry(2560, 0, 1920, 1080, Orientation.Landscape), "\\\\.\\D2", false, "DEL", "U24", "SN-RIGHT"),
        }, DateTimeOffset.UnixEpoch);
        m.ConfigureDisplay(profile);
        return m;
    }

    [Fact]
    public async Task Add_then_get_round_trips_the_full_graph()
    {
        var machine = BuildConfigured();
        await _repo.AddAsync(machine, _ct);

        var loaded = await _repo.GetAsync(machine.Id, _ct);

        Assert.NotNull(loaded);
        Assert.Equal("build-agent-01", loaded!.Name);
        Assert.Equal("10.0.4.21", loaded.Host.Host);
        Assert.Equal(CredentialStoreKind.Dpapi, loaded.Credential.Kind);
        Assert.Equal(DisplayMode.MultiMonitor, loaded.DisplayMode);
        Assert.Equal(2, loaded.DisplayProfile!.SelectedMonitors.Count);
        Assert.Equal("SN-CENTER", loaded.DisplayProfile.SelectedMonitors[0].EdidSerial);
        Assert.Contains(loaded.Tags, t => t.Name == "CI");
    }

    [Fact]
    public async Task Get_returns_null_for_unknown_id()
        => Assert.Null(await _repo.GetAsync(Guid.NewGuid(), _ct));

    [Fact]
    public async Task List_orders_by_name()
    {
        await _repo.AddAsync(new Machine("zeta", HostAddress.Create("10.0.0.9"), DateTimeOffset.UnixEpoch), _ct);
        await _repo.AddAsync(new Machine("alpha", HostAddress.Create("10.0.0.1"), DateTimeOffset.UnixEpoch), _ct);

        var all = await _repo.ListAsync(_ct);

        Assert.Equal(new[] { "alpha", "zeta" }, all.Select(m => m.Name).ToArray());
    }

    [Fact]
    public async Task Remove_deletes_machine_and_cascades_profile()
    {
        var machine = BuildConfigured();
        await _repo.AddAsync(machine, _ct);

        await _repo.RemoveAsync(machine.Id, _ct);

        Assert.Null(await _repo.GetAsync(machine.Id, _ct));
    }

    [Fact]
    public async Task No_plaintext_secret_column_exists()
    {
        using var db = _factory.CreateDbContext();
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(_ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM pragma_table_info('Machines');";
        var columns = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync(_ct);
        while (await reader.ReadAsync(_ct)) columns.Add(reader.GetString(0));

        Assert.DoesNotContain(columns, c => c.Contains("Password", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("CredentialRef", columns); // we store a reference, not a secret
    }

    public void Dispose() => _factory.Dispose();
}
