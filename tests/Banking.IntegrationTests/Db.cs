using Npgsql;
using Xunit;

namespace Banking.IntegrationTests;

internal static class Db
{
    public static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    public static async Task<int> ExecuteAsync(
        NpgsqlConnection connection, string sql, NpgsqlTransaction? transaction = null, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(connection, sql, transaction, parameters);
        return await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public static async Task<T> ScalarAsync<T>(
        NpgsqlConnection connection, string sql, NpgsqlTransaction? transaction = null, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(connection, sql, transaction, parameters);
        return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    public static async Task<PostgresException> AssertFailsAsync(Func<Task> action, string expectedSqlState)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(expectedSqlState, error.SqlState);
        return error;
    }

    private static NpgsqlCommand Command(
        NpgsqlConnection connection, string sql, NpgsqlTransaction? transaction, (string Name, object Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command;
    }
}
