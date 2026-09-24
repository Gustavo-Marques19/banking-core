using Banking.Application.Accounts;
using Banking.Application.Audit;
using Banking.Application.Customers;
using Banking.Application.Deposits;
using Banking.Application.ExternalTransfers;
using Banking.Application.Reconciliation;
using Banking.Application.Reversals;
using Banking.Application.Idempotency;
using Banking.Application.LedgerQueries;
using Banking.Application.Notifications;
using Banking.Application.Transfers;
using Banking.Domain.Common;
using Banking.Domain.Payments;
using Banking.Infrastructure.Security;

namespace Banking.Api.Composition;

public sealed class LimitsOptions
{
    public const string SectionName = "Limits";

    public string DepositPerOperation { get; set; } = "50000.00";

    public string DepositDailyPerOperator { get; set; } = "200000.00";

    /// <summary>Acima deste valor, o depósito espera a aprovação de outro operador.</summary>
    public string DepositApprovalThreshold { get; set; } = "10000.00";

    public string TransferPerTransaction { get; set; } = "20000.00";

    public string TransferDailyPerAccount { get; set; } = "50000.00";
}

internal static class ApplicationSetup
{
    public static IServiceCollection AddBankingApplication(this IServiceCollection services, IConfiguration configuration)
    {
        var limits = configuration.GetSection(LimitsOptions.SectionName).Get<LimitsOptions>() ?? new LimitsOptions();
        services.AddSingleton(new DepositLimits(
            Money.Parse(limits.DepositPerOperation, Currency.Brl),
            Money.Parse(limits.DepositDailyPerOperator, Currency.Brl),
            Money.Parse(limits.DepositApprovalThreshold, Currency.Brl)));
        services.AddSingleton(new TransferLimits(
            Money.Parse(limits.TransferPerTransaction, Currency.Brl),
            Money.Parse(limits.TransferDailyPerAccount, Currency.Brl)));

        services.AddSingleton(configuration.GetSection(PiiOptions.SectionName).Get<PiiOptions>() ?? new PiiOptions());

        services.AddScoped<IdempotentExecutor>();
        services.AddScoped<AccountAccess>();
        services.AddScoped<RegisterCustomerHandler>();
        services.AddScoped<GetCustomerHandler>();
        services.AddScoped<OpenAccountHandler>();
        services.AddScoped<AccountQueriesHandler>();
        services.AddScoped<AccountStatusHandler>();
        services.AddScoped<MakeDepositHandler>();
        services.AddScoped<CreateTransferHandler>();
        services.AddScoped<GetTransferHandler>();
        services.AddScoped<GetLedgerTransactionHandler>();
        services.AddScoped<NotificationQueriesHandler>();
        services.AddScoped<CreateExternalTransferHandler>();
        services.AddScoped<ExternalTransferQueriesHandler>();
        services.AddScoped<CancelExternalTransferHandler>();
        services.AddScoped<ExternalTransferProcessor>();
        services.AddScoped<ProviderWebhookHandler>();
        services.AddScoped<ReconciliationHandler>();
        services.AddScoped<AuditHandler>();
        services.AddScoped<DepositApprovalHandler>();
        services.AddScoped<ReversalHandler>();
        services.AddSingleton(new ExternalTransferSettings(
            configuration.GetValue("ExternalTransfers:CheckInterval", TimeSpan.FromSeconds(5)),
            configuration.GetValue("ExternalTransfers:MaxSubmitAttempts", 5)));

        return services;
    }
}
