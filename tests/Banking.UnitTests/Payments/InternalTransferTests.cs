using Banking.Domain.Accounts;
using Banking.Domain.Common;
using Banking.Domain.Ledger;
using Banking.Domain.Payments;
using Banking.UnitTests.Accounts;
using Xunit;

namespace Banking.UnitTests.Payments;

public sealed class InternalTransferTests
{
    private static readonly TransferLimits Limits = new(Brl(20_000_00), Brl(50_000_00));

    private readonly Account _source = Account.Open(TestCustomers.Active(), 1, Currency.Brl, TestCustomers.Now).Account;
    private readonly Account _destination = Account.Open(TestCustomers.Active(), 2, Currency.Brl, TestCustomers.Now).Account;

    [Fact]
    public void Com_saldo_lanca_d_origem_c_destino()
    {
        var (transfer, posting) = Decide(balance: 100_00, amount: 100_00);

        Assert.Equal(TransferStatus.Completed, transfer.Status);
        Assert.NotNull(posting);
        Assert.Contains(posting.Entries, e => e.LedgerAccountId == _source.LedgerAccountId && e.Direction == EntryDirection.Debit);
        Assert.Contains(posting.Entries, e => e.LedgerAccountId == _destination.LedgerAccountId && e.Direction == EntryDirection.Credit);
    }

    [Fact]
    public void Sem_saldo_e_recusada()
    {
        var (transfer, posting) = Decide(balance: 99_99, amount: 100_00);

        Assert.Equal(RejectionReason.InsufficientFunds, transfer.RejectionReason);
        Assert.Null(posting);
    }

    [Fact]
    public void Destino_bloqueado_nao_revela_o_motivo()
    {
        _destination.Block();

        var (transfer, _) = Decide(balance: 100_00, amount: 1_00);

        Assert.Equal(RejectionReason.DestinationUnavailable, transfer.RejectionReason);
    }

    [Fact]
    public void Limite_diario_vem_antes_do_saldo()
    {
        var (transfer, _) = Decide(balance: 0, amount: 1_00, usedToday: 50_000_00);

        Assert.Equal(RejectionReason.DailyLimitExceeded, transfer.RejectionReason);
    }

    [Fact]
    public void Mesma_conta_e_erro_de_validacao()
    {
        var error = Assert.Throws<DomainException>(() => InternalTransfer.Decide(
            _source, _source, Balance(100_00), Brl(1_00), null, "sub", "key", Brl(0), Limits, TestCustomers.Now));

        Assert.Equal("same_account", error.Code);
    }

    private (InternalTransfer Transfer, LedgerTransaction? Posting) Decide(long balance, long amount, long usedToday = 0) =>
        InternalTransfer.Decide(
            _source, _destination, Balance(balance), Brl(amount), null, "sub", "key", Brl(usedToday), Limits, TestCustomers.Now);

    private LedgerBalance Balance(long minor) => new(_source.LedgerAccountId, Brl(minor), EntryDirection.Credit, AllowNegative: false);

    private static Money Brl(long minor) => Money.FromMinor(minor, Currency.Brl);
}
