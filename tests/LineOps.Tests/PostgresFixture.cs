using LineOps.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace LineOps.Tests;

/// <summary>
/// A disposable Postgres instance shared by the integration tests.
///
/// These tests run against real Postgres rather than an in-memory provider on purpose:
/// the parts most worth testing — native range partitioning, jsonb columns, timestamptz
/// offset handling — do not exist in the in-memory provider, so a green suite there would
/// prove nothing about the schema that actually ships.
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("lineops_test")
        .WithUsername("lineops")
        .WithPassword("lineops_test")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public LineOpsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LineOpsDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new LineOpsDbContext(options);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
