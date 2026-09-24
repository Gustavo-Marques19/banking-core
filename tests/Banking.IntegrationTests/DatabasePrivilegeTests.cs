using Banking.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace Banking.IntegrationTests;

/// <summary>
/// A role da aplicação não pode alterar estrutura. Se alguém invadir a API, não consegue criar, apagar ou ler o que não deve.
/// </summary>
public sealed class DatabasePrivilegeTests(PostgresFixture postgres)
{
    public static TheoryData<string> Schemas =>
    [
        DatabaseSchemas.Accounts,
        DatabaseSchemas.Ledger,
        DatabaseSchemas.Payments,
        DatabaseSchemas.Platform,
        DatabaseSchemas.Notifications,
        "public",
    ];

    [Fact]
    public async Task App_conecta_com_a_propria_role()
    {
        var user = await ScalarAsync(postgres.AppConnectionString, "SELECT current_user");

        Assert.Equal(DatabaseRoles.App, user);
    }

    [Theory]
    [MemberData(nameof(Schemas))]
    public async Task App_nao_cria_tabela(string schema)
    {
        await AssertInsufficientPrivilegeAsync($"CREATE TABLE {schema}.probe (id int)");
    }

    [Fact]
    public async Task App_nao_apaga_tabela()
    {
        await AssertInsufficientPrivilegeAsync("DROP TABLE platform.__ef_migrations_history");
    }

    [Fact]
    public async Task App_nao_apaga_schema()
    {
        await AssertInsufficientPrivilegeAsync($"DROP SCHEMA {DatabaseSchemas.Ledger}");
    }

    [Fact]
    public async Task App_nao_le_o_historico_de_migrations()
    {
        await AssertInsufficientPrivilegeAsync("SELECT count(*) FROM platform.__ef_migrations_history");
    }

    [Fact]
    public async Task Migrator_e_dono_dos_schemas_dos_modulos()
    {
        await using var connection = await OpenAsync(postgres.MigratorConnectionString);
        await using var command = new NpgsqlCommand(
            "SELECT nspname, pg_get_userbyid(nspowner) FROM pg_namespace WHERE nspname = ANY(@schemas) ORDER BY nspname",
            connection);
        command.Parameters.AddWithValue("schemas", new[]
        {
            DatabaseSchemas.Accounts, DatabaseSchemas.Ledger, DatabaseSchemas.Payments, DatabaseSchemas.Platform,
        });

        var owners = new List<(string Schema, string Owner)>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            owners.Add((reader.GetString(0), reader.GetString(1)));
        }

        Assert.Equal(4, owners.Count);
        Assert.All(owners, o => Assert.Equal(DatabaseRoles.Migrator, o.Owner));
    }

    private async Task AssertInsufficientPrivilegeAsync(string sql)
    {
        await using var connection = await OpenAsync(postgres.AppConnectionString);
        await using var command = new NpgsqlCommand(sql, connection);

        var error = await Assert.ThrowsAsync<PostgresException>(
            () => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    private static async Task<string?> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var command = new NpgsqlCommand(sql, connection);
        return (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }
}
