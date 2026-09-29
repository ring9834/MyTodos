using Testcontainers.PostgreSql;
using Xunit;

namespace Todo.IntegrationTests;

// A real, ephemeral PostgreSQL per test run, not SQLite in-memory — SQLite has
// no xmin equivalent, so Q6's concurrency test could not be honestly written against it.
// Shared across the whole test run (not spun up per-test) for CI speed.
public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("tododb")
        .WithUsername("todo")
        .WithPassword("test")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture> { }
