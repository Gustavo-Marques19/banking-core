using Banking.Domain.Accounts;
using Banking.Domain.Common;
using Banking.Domain.Ledger;
using Banking.Domain.Payments;
using Banking.UnitTests.Accounts;
using Xunit;

namespace Banking.UnitTests.Payments;

public sealed class DepositTests
{
    private static readonly DepositLimits Limits = new(Brl(50_000_00), Brl(200_000_00));

    [Fact]
    public void Deposito_aceito_lanca_d_funding_c_cliente()
    {
        var account = OpenAccount();

        var (deposit, posting) = Decide(account, Brl(1_000_00), usedToday: Brl(0));

        Assert.Equal(DepositStatus.Completed, deposit.Status);
        Assert.NotNull(posting);
        Assert.Contains(posting.Entries, e => e.LedgerAccountId == SystemLedgerAccounts.Funding && e.Direction == EntryDirection.Debit);
        Assert.Contains(posting.Entries, e => e.LedgerAccountId == account.LedgerAccountId && e.Direction == EntryDirection.Credit);
        Assert.Equal($"deposit:{deposit.Id}", posting.ExternalId);
    }

    [Fact]
    public void Acima_do_limite_por_operacao_e_recusado_sem_lancamento()
    {
        var (deposit, posting) = Decide(OpenAccount(), Brl(50_000_01), usedToday: Brl(0));

        Assert.Equal(RejectionReason.LimitExceeded, deposit.RejectionReason);
        Assert.Null(posting);
    }

    [Fact]
    public void Limite_diario_considera_o_que_o_operador_ja_depositou()
    {
        var (deposit, _) = Decide(OpenAccount(), Brl(1), usedToday: Brl(200_000_00));

        Assert.Equal(RejectionReason.DailyLimitExceeded, deposit.RejectionReason);
    }

    [Fact]
    public void Conta_bloqueada_recusa_deposito()
    {
        var account = OpenAccount();
        account.Block();

        var (deposit, _) = Decide(account, Brl(100), usedToday: Brl(0));

        Assert.Equal(RejectionReason.AccountBlocked, deposit.RejectionReason);
    }

    private static Account OpenAccount() => Account.Open(TestCustomers.Active(), 1, Currency.Brl, TestCustomers.Now).Account;

    private static (Deposit Deposit, LedgerTransaction? Posting) Decide(Account account, Money amount, Money usedToday) =>
        Deposit.Decide(account, amount, "aporte", "operator-1", "key-1", usedToday, Limits, TestCustomers.Now);

    private static Money Brl(long minor) => Money.FromMinor(minor, Currency.Brl);
}
