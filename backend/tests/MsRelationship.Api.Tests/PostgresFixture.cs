using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using Npgsql;
using Testcontainers.PostgreSql;

namespace MsRelationship.Api.Tests;

/// One container for the whole test run. Tests that touch identity, merge or
/// history are exactly the ones an in-memory provider gets wrong, so they run
/// against the real thing.
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:16").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);

    /// A second, empty database on the same container, already migrated. Every
    /// other class in this collection shares the one database above, so a test
    /// that asserts exact row counts (SeedTests) needs a database of its own to
    /// start from empty. One container per test run stays true — this adds a
    /// database to it, not a second container.
    public async Task<AppDbContext> CreateIsolatedDatabaseAsync()
    {
        var isolated = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = $"seed_{Guid.NewGuid():N}"
        };

        await using (var admin = new NpgsqlConnection(ConnectionString))
        {
            await admin.OpenAsync();
            await using var createDb = admin.CreateCommand();
            createDb.CommandText = $"CREATE DATABASE \"{isolated.Database}\"";
            await createDb.ExecuteNonQueryAsync();
        }

        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(isolated.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);
        await db.Database.MigrateAsync();
        return db;
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture> { }
