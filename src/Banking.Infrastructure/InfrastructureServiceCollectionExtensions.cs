using Banking.Application.Abstractions;
using Banking.Application.Idempotency;
using Banking.Domain.Accounts;
using Banking.Infrastructure.Accounts;
using Banking.Application.Audit;
using Banking.Application.Notifications;
using Banking.Application.Reconciliation;
using Banking.Infrastructure.Audit;
using Banking.Infrastructure.Ledger;
using Banking.Infrastructure.Messaging;
using Banking.Infrastructure.Notifications;
using Banking.Infrastructure.Payments;
using Banking.Infrastructure.Persistence;
using Banking.Infrastructure.Platform;
using Banking.Infrastructure.Provider;
using Banking.Infrastructure.Reconciliation;
using Banking.Infrastructure.Telemetry;
using Microsoft.Extensions.Options;
using Banking.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

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
        services.AddScoped<ITransferReversalRepository, TransferReversalRepository>();
        services.AddScoped<ILimitUsageStore, LimitUsageStore>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddSingleton<IDocumentProtector>(sp => new AesGcmDocumentProtector(sp.GetRequiredService<PiiOptions>()));

        services.AddScoped<IExternalTransferRepository, ExternalTransferRepository>();
        services.AddScoped<IInbox, InboxStore>();
        services.AddScoped<IReconciliationService, ReconciliationService>();
        services.AddSingleton<MockBankingProvider>();
        services.AddSingleton<IBankingProvider>(sp => sp.GetRequiredService<IOptions<ProviderOptions>>().Value.IsMock
            ? new ResilientBankingProvider(sp.GetRequiredService<MockBankingProvider>(), sp.GetRequiredService<IOptions<ProviderOptions>>())
            : throw new InvalidOperationException("Provider:Mode desconhecido. A Fase 1 só tem o mock."));
        services.AddSingleton<ExternalTransferWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<ExternalTransferWorker>());

        services.AddScoped<IAuditTrail, AuditTrail>();
        services.AddScoped<IAuditQueries, AuditQueries>();
        services.AddScoped<IOutbox, Outbox>();
        services.AddScoped<INotificationReadModel, NotificationReadModel>();
        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();
        services.AddSingleton<OutboxPublisher>();
        services.AddHostedService(sp => sp.GetRequiredService<OutboxPublisher>());
        services.AddSingleton<NotificationsConsumer>();
        services.AddHostedService(sp => sp.GetRequiredService<NotificationsConsumer>());

        services.AddHostedService<OperationalGaugesWorker>();
        services.AddSingleton<ReconciliationWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<ReconciliationWorker>());

        services.AddSingleton<IdempotencyKeyCleanup>();
        services.AddHostedService(sp => sp.GetRequiredService<IdempotencyKeyCleanup>());

        services.AddHealthChecks()
            .AddDbContextCheck<BankingDbContext>("postgres", tags: [ReadinessTag])
            .AddCheck<RabbitMqHealthCheck>(
                "rabbitmq", failureStatus: HealthStatus.Degraded, tags: [ReadinessTag], timeout: TimeSpan.FromSeconds(3));

        return services;
    }
}
