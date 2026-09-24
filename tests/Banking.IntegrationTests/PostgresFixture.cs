using Banking.Infrastructure.Persistence;
using Banking.IntegrationTests;
using Banking.IntegrationTests.Support;
using DotNet.Testcontainers.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

[assembly: AssemblyFixture(typeof(PostgresFixture))]

namespace Banking.IntegrationTests;

/// <summary>
/// Um Postgres por execução, criado pelo mesmo script de roles do devcontainer e migrado com a role banking_migrator.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    // Mesma imagem de .devcontainer/docker-compose.yml.
    private const string Image = "postgres:18.6-alpine";

    private readonly string _migratorPassword = Guid.NewGuid().ToString("N");
    private readonly string _appPassword = Guid.NewGuid().ToString("N");
    private readonly PostgreSqlContainer _container;
    private readonly Lazy<ApiFactory> _api;

    public PostgresFixture()
    {
        _container = new PostgreSqlBuilder(Image)
            .WithCommand("-c", "max_connections=400")
            .WithEnvironment("BANKING_MIGRATOR_PASSWORD", _migratorPassword)
            .WithEnvironment("BANKING_APP_PASSWORD", _appPassword)
            .WithResourceMapping(
                new FileInfo(RepositoryPaths.PostgresInitScript),
                "/docker-entrypoint-initdb.d/",
                fileMode: UnixFileModes.UserRead | UnixFileModes.UserWrite | UnixFileModes.UserExecute
                    | UnixFileModes.GroupRead | UnixFileModes.GroupExecute
                    | UnixFileModes.OtherRead | UnixFileModes.OtherExecute)
            .Build();
        _api = new Lazy<ApiFactory>(() => new ApiFactory(AppConnectionString));
    }

    /// <summary>API compartilhada pelos testes que não precisam de configuração própria.</summary>
    public ApiFactory Api => _api.Value;

    public ApiFactory CreateApi(IReadOnlyDictionary<string, string?> settings) => new(AppConnectionString, settings);

    public string MigratorConnectionString => ConnectionStringFor(DatabaseRoles.Migrator, _migratorPassword);

    public string AppConnectionString => ConnectionStringFor(DatabaseRoles.App, _appPassword);

    /// <summary>Superusuário do container, para simular quem tem acesso total ao banco.</summary>
    public string SuperuserConnectionString =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = "banking" }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await MigrateAsync(MigratorConnectionString);
    }

    /// <summary>
    /// Banco novo no mesmo servidor, com as mesmas roles e migrations. Para testes com workers (outbox, provider),
    /// que processam tudo o que encontram no banco.
    /// </summary>
    public async Task<IsolatedDatabase> CreateIsolatedDatabaseAsync()
    {
        var name = "banking_" + Guid.NewGuid().ToString("N")[..12];
        await using (var server = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await server.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name} OWNER {DatabaseRoles.Migrator}", server);
            await create.ExecuteNonQueryAsync();
        }

        await using (var database = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString))
        {
            await database.OpenAsync();
            await using var grants = new NpgsqlCommand(
                $"""
                REVOKE ALL ON DATABASE {name} FROM PUBLIC;
                GRANT CONNECT ON DATABASE {name} TO {DatabaseRoles.App};
                REVOKE ALL ON SCHEMA public FROM PUBLIC;
                """,
                database);
            await grants.ExecuteNonQueryAsync();
        }

        var isolated = new IsolatedDatabase(
            ConnectionStringFor(DatabaseRoles.App, _appPassword, name),
            ConnectionStringFor(DatabaseRoles.Migrator, _migratorPassword, name),
            new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString);
        await MigrateAsync(isolated.MigratorConnectionString);
        return isolated;
    }

    private static async Task MigrateAsync(string migratorConnectionString)
    {
        var options = new DbContextOptionsBuilder<BankingDbContext>();
        BankingDbContextOptions.Configure(options, migratorConnectionString);
        await using var db = new BankingDbContext(options.Options);
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_api.IsValueCreated)
        {
            await _api.Value.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    private string ConnectionStringFor(string username, string password, string database = "banking") =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = database,
            Username = username,
            Password = password,
        }.ConnectionString;
}

public sealed record IsolatedDatabase(string AppConnectionString, string MigratorConnectionString, string SuperuserConnectionString);
