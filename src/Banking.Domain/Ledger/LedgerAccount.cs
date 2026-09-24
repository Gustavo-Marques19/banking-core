using Banking.Domain.Common;

namespace Banking.Domain.Ledger;

/// <summary>Conta contábil (ADR-003). Separada da conta do cliente, que vive no módulo Accounts.</summary>
public sealed class LedgerAccount
{
    private LedgerAccount()
    {
        Code = string.Empty;
        Name = string.Empty;
        CurrencyCode = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public LedgerAccountNature Nature { get; private set; }

    public EntryDirection NormalBalance { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>Conta do cliente associada. Nulo em contas de sistema.</summary>
    public Guid? AccountId { get; private set; }

    public bool AllowNegative { get; private set; }

    public bool HasMaterializedBalance { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Currency Currency => Currency.FromCode(CurrencyCode);

    public static LedgerAccount OpenForCustomerAccount(
        Guid id, Guid accountId, string accountNumber, Currency currency, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountNumber);

        return new LedgerAccount
        {
            Id = id,
            Code = $"2.1.{accountNumber}",
            Name = $"Depósito do cliente, conta {accountNumber}",
            Nature = LedgerAccountNature.Liability,
            NormalBalance = EntryDirection.Credit,
            CurrencyCode = currency.Code,
            AccountId = accountId,
            AllowNegative = false,
            HasMaterializedBalance = true,
            CreatedAt = now,
        };
    }
}
