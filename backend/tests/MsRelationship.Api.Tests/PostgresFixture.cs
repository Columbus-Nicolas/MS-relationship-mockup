using Microsoft.EntityFrameworkCore;
using MsRelationship.Api.Data;
using Testcontainers.PostgreSql;

namespace MsRelationship.Api.Tests;

/// One container for the whole test run. Tests that touch identity, merge or
/// history are exactly the ones an in-memory provider gets wrong, so they run
/// against the real thing.
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder().WithImage("postgres:16").Build();

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

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture> { }
