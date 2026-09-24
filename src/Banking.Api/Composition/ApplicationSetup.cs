using Banking.Application.Accounts;
using Banking.Application.Customers;
using Banking.Application.Deposits;
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
            Money.Parse(limits.DepositDailyPerOperator, Currency.Brl)));
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

        return services;
    }
}
