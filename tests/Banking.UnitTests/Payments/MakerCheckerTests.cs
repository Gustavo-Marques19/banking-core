using Banking.Domain.Accounts;
using Banking.Domain.Common;
using Banking.Domain.Ledger;
using Banking.Domain.Payments;
using Banking.UnitTests.Accounts;
using Xunit;

namespace Banking.UnitTests.Payments;

public sealed class MakerCheckerTests
{
    private static readonly DepositLimits Limits = new(Brl(50_000_00), Brl(200_000_00), Brl(10_000_00));

    private readonly Account _account = Account.Open(TestCustomers.Active(), 1, Currency.Brl, TestCustomers.Now).Account;

    [Fact]
    public void Deposito_acima_do_limite_de_aprovacao_fica_pendente_sem_lancamento()
    {
        var (deposit, posting) = Deposit.Decide(_account, Brl(10_000_01), "aporte", "maker", "key", Brl(0), Limits, TestCustomers.Now);

        Assert.Equal(DepositStatus.PendingApproval, deposit.Status);
        Assert.Null(posting);
    }

    [Fact]
    public void Quem_pediu_nao_aprova()
    {
        var (deposit, _) = Deposit.Decide(_account, Brl(10_000_01), "aporte", "maker", "key", Brl(0), Limits, TestCustomers.Now);

        var error = Assert.Throws<DomainException>(() => deposit.Approve("maker", _account, Brl(0), Limits, TestCustomers.Now));

        Assert.Equal("self_approval_not_allowed", error.Code);
    }

    [Fact]
    public void Aprovacao_confere_a_conta_de_novo()
    {
        var (deposit, _) = Deposit.Decide(_account, Brl(10_000_01), "aporte", "maker", "key", Brl(0), Limits, TestCustomers.Now);
        _account.Block();

        var posting = deposit.Approve("checker", _account, Brl(0), Limits, TestCustomers.Now);

        Assert.Null(posting);
        Assert.Equal(RejectionReason.AccountBlocked, deposit.RejectionReason);
    }

    [Fact]
    public void Aprovacao_lanca_e_registra_quem_aprovou()
    {
        var (deposit, _) = Deposit.Decide(_account, Brl(10_000_01), "aporte", "maker", "key", Brl(0), Limits, TestCustomers.Now);

        var posting = deposit.Approve("checker", _account, Brl(0), Limits, TestCustomers.Now);

        Assert.NotNull(posting);
        Assert.Equal(DepositStatus.Completed, deposit.Status);
        Assert.Equal("checker", deposit.DecidedBy);
    }

    [Fact]
    public void Estorno_recusado_quando_o_destinatario_nao_tem_saldo()
    {
        var destination = Account.Open(TestCustomers.Active(), 2, Currency.Brl, TestCustomers.Now).Account;
        var (transfer, original) = InternalTransfer.Decide(
            _account,
            destination,
            new LedgerBalance(_account.LedgerAccountId, Brl(100_00), EntryDirection.Credit, false),
            Brl(100_00),
            null,
            "alice",
            "key",
            Brl(0),
            new TransferLimits(Brl(20_000_00), Brl(50_000_00)),
            TestCustomers.Now);
        var reversal = TransferReversal.Request(transfer, "maker", "contestação", TestCustomers.Now);

        var posting = reversal.Approve(
            "checker",
            original!,
            new LedgerBalance(destination.LedgerAccountId, Brl(99_99), EntryDirection.Credit, false),
            destination.LedgerAccountId,
            TestCustomers.Now);

        Assert.Null(posting);
        Assert.Equal(RejectionReason.InsufficientFunds, reversal.RejectionReason);
    }

    private static Money Brl(long minor) => Money.FromMinor(minor, Currency.Brl);
}
