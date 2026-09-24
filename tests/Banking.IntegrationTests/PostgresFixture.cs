using Banking.Infrastructure.Persistence;
using Banking.IntegrationTests;
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

    public PostgresFixture()
    {
        _container = new PostgreSqlBuilder(Image)
            .WithEnvironment("BANKING_MIGRATOR_PASSWORD", _migratorPassword)
            .WithEnvironment("BANKING_APP_PASSWORD", _appPassword)
            .WithResourceMapping(
                new FileInfo(RepositoryPaths.PostgresInitScript),
                "/docker-entrypoint-initdb.d/",
                fileMode: UnixFileModes.UserRead | UnixFileModes.UserWrite | UnixFileModes.UserExecute
                    | UnixFileModes.GroupRead | UnixFileModes.GroupExecute
                    | UnixFileModes.OtherRead | UnixFileModes.OtherExecute)
            .Build();
    }

    public string MigratorConnectionString => ConnectionStringFor(DatabaseRoles.Migrator, _migratorPassword);

    public string AppConnectionString => ConnectionStringFor(DatabaseRoles.App, _appPassword);

    /// <summary>Superusuário do container, para simular quem tem acesso total ao banco.</summary>
    public string SuperuserConnectionString =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = "banking" }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<BankingDbContext>();
        BankingDbContextOptions.Configure(options, MigratorConnectionString);
        await using var db = new BankingDbContext(options.Options);
        await db.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    private string ConnectionStringFor(string username, string password) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = "banking",
            Username = username,
            Password = password,
        }.ConnectionString;
}
