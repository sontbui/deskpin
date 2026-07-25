using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RdpManager.Infrastructure.Persistence;

namespace RdpManager.Infrastructure.Tests;

/// <summary>
/// A self-contained SQLite factory for tests. Uses a shared in-memory connection that stays
/// open for the fixture's lifetime, so schema + data persist across the short-lived contexts
/// the repository creates.
/// </summary>
public sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public TestDbContextFactory()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var ctx = new AppDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public AppDbContext CreateDbContext() => new(_options);

    public void Dispose() => _connection.Dispose();
}
