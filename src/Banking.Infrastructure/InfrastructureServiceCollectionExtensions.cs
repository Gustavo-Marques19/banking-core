using Banking.Application.Abstractions;
using Banking.Application.Idempotency;
using Banking.Domain.Accounts;
using Banking.Infrastructure.Accounts;
using Banking.Infrastructure.Ledger;
using Banking.Infrastructure.Payments;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Platform;
using Banking.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Banking.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public const string ReadinessTag = "ready";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<BankingDbContext>(options =>
            BankingDbContextOptions.Configure(options, connectionString));

        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<ILedger, LedgerRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IAccountReadModel, AccountReadModel>();
        services.AddScoped<IDepositRepository, DepositRepository>();
        services.AddScoped<ITransferRepository, TransferRepository>();
        services.AddScoped<ILimitUsageStore, LimitUsageStore>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddSingleton<IDocumentProtector>(sp => new AesGcmDocumentProtector(sp.GetRequiredService<PiiOptions>()));

        services.AddSingleton<IdempotencyKeyCleanup>();
        services.AddHostedService(sp => sp.GetRequiredService<IdempotencyKeyCleanup>());

        services.AddHealthChecks()
            .AddDbContextCheck<BankingDbContext>("postgres", tags: [ReadinessTag]);

        return services;
    }
}
