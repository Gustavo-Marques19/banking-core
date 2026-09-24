using Microsoft.EntityFrameworkCore;

namespace Banking.Infrastructure.Persistence;

public static class BankingDbContextOptions
{
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder builder, string connectionString) =>
        builder
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", DatabaseSchemas.Platform))
            .UseSnakeCaseNamingConvention();
}
