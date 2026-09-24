using Banking.Application.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Banking.Infrastructure.Persistence;

internal static class PostgresErrors
{
    public static async Task<T> TranslateAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception error) when (Translate(error) is { } translated)
        {
            throw translated;
        }
    }

    public static async Task TranslateAsync(Func<Task> action) =>
        await TranslateAsync(async () =>
        {
            await action();
            return true;
        });

    private static Exception? Translate(Exception error)
    {
        var postgres = error as PostgresException ?? (error as DbUpdateException)?.InnerException as PostgresException;
        return postgres?.SqlState switch
        {
            PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure
                => new ContentionException("Conta ocupada por outra operação. Tente de novo.", postgres),
            PostgresErrorCodes.UniqueViolation => new UniqueConstraintException(postgres.ConstraintName ?? "unknown", postgres),
            _ => null,
        };
    }
}
