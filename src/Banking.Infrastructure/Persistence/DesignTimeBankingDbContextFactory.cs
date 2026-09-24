using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Banking.Infrastructure.Persistence;

/// <summary>
/// Usada só pelo dotnet-ef. Migrations rodam com a role banking_migrator, nunca com a role da aplicação.
/// </summary>
internal sealed class DesignTimeBankingDbContextFactory : IDesignTimeDbContextFactory<BankingDbContext>
{
    public BankingDbContext CreateDbContext(string[] args)
    {
        // "migrations add" não abre conexão, então o fallback sem senha basta para gerar arquivos.
        var connectionString = Environment.GetEnvironmentVariable("BANKING_MIGRATOR_CONNECTION")
            ?? "Host=localhost;Database=banking;Username=banking_migrator";

        var builder = new DbContextOptionsBuilder<BankingDbContext>();
        BankingDbContextOptions.Configure(builder, connectionString);
        return new BankingDbContext(builder.Options);
    }
}
