using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using Testcontainers.PostgreSql;
using Xunit;

namespace MsRelationship.Api.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>
    /// Truncates every application table (schema and migrations stay intact) so each test
    /// method starts from an empty database. Table names are read from the EF model, so
    /// new entities added in later tasks are picked up automatically. Call this from
    /// <c>IAsyncLifetime.InitializeAsync</c> in each test class sharing this fixture.
    /// </summary>
    public async Task ResetAsync()
    {
        await using var db = NewContext();
        var tables = db.Model.GetEntityTypes()
            .Select(t => t.GetTableName())
            .Where(name => name is not null)
            .Distinct();
        var tableList = string.Join(", ", tables.Select(name => $"\"{name}\""));
        var sql = $"TRUNCATE TABLE {tableList} RESTART IDENTITY CASCADE;";
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
