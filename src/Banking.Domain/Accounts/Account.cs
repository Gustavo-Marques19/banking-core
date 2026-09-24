using Banking.Domain.Common;
using Banking.Domain.Ledger;

namespace Banking.Domain.Accounts;

/// <summary>Conta do cliente. O dinheiro dela vive na <see cref="LedgerAccount"/> associada (ADR-003).</summary>
public sealed class Account
{
    public const string DefaultBranch = "0001";

    private Account()
    {
        Branch = string.Empty;
        Number = string.Empty;
        CurrencyCode = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public string Branch { get; private set; }

    public string Number { get; private set; }

    public string CurrencyCode { get; private set; }

    public AccountStatus Status { get; private set; }

    public Guid LedgerAccountId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Currency Currency => Currency.FromCode(CurrencyCode);

    /// <summary>Abre a conta e a conta contábil correspondente. As duas são gravadas na mesma transação.</summary>
    public static (Account Account, LedgerAccount LedgerAccount) Open(
        Customer customer, long sequenceNumber, Currency currency, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(customer);
        if (customer.Status != CustomerStatus.Active)
        {
            throw new DomainException("customer_not_active", "Cliente inativo não abre conta.");
        }

        var id = Guid.CreateVersion7(now);
        var number = sequenceNumber.ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
        var ledgerAccount = LedgerAccount.OpenForCustomerAccount(Guid.CreateVersion7(now), id, number, currency, now);

        var account = new Account
        {
            Id = id,
            CustomerId = customer.Id,
            Branch = DefaultBranch,
            Number = number,
            CurrencyCode = currency.Code,
            Status = AccountStatus.Active,
            LedgerAccountId = ledgerAccount.Id,
            CreatedAt = now,
        };

        return (account, ledgerAccount);
    }

    public void Block()
    {
        if (Status == AccountStatus.Closed)
        {
            throw new DomainException("account_closed", "Conta encerrada não pode ser bloqueada.");
        }

        Status = AccountStatus.Blocked;
    }

    public void Unblock()
    {
        if (Status == AccountStatus.Closed)
        {
            throw new DomainException("account_closed", "Conta encerrada não pode ser desbloqueada.");
        }

        Status = AccountStatus.Active;
    }

    /// <summary>Motivo de recusa para movimentar a conta, ou nulo se ela pode movimentar.</summary>
    public RejectionReason? RejectionForMovement() => Status switch
    {
        AccountStatus.Active => null,
        AccountStatus.Blocked => RejectionReason.AccountBlocked,
        _ => RejectionReason.AccountClosed,
    };
}
