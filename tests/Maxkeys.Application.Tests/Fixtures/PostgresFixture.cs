using Maxkeys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Maxkeys.Application.Tests.Fixtures;

/// <summary>
/// Real Postgres per test run (ADR-09 — InMemory/SQLite cannot exercise
/// <c>FOR UPDATE SKIP LOCKED</c>, <c>xmin</c>, <c>jsonb</c>, or partial unique indexes).
/// If the <c>TEST_POSTGRES_CONNECTION</c> environment variable is set, it is used
/// instead of starting a container — for environments where Docker is unavailable.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer? _container;
    private readonly string? _externalConnectionString;

    public PostgresFixture()
    {
        _externalConnectionString = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");
        if (_externalConnectionString is null)
        {
            _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        }
    }

    public string ConnectionString => _externalConnectionString ?? _container!.GetConnectionString();

    public async Task InitializeAsync()
    {
        if (_container is not null)
        {
            await _container.StartAsync();
        }

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new AppDbContext(options);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
