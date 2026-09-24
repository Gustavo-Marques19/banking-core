using Banking.Domain.Accounts;
using Banking.Domain.Common;
using Banking.Domain.Ledger;
using Banking.Domain.Payments;
using Banking.UnitTests.Accounts;
using Xunit;

namespace Banking.UnitTests.Payments;

public sealed class ExternalTransferTests
{
    private static readonly TransferLimits Limits = new(Brl(20_000_00), Brl(50_000_00));
    private static readonly TimeSpan CheckAfter = TimeSpan.FromSeconds(5);
    private static readonly ExternalDestination Destination = new("00000000", "0001", "12345");

    private readonly Account _source = Account.Open(TestCustomers.Active(), 1, Currency.Brl, TestCustomers.Now).Account;

    [Fact]
    public void Criacao_reserva_em_clearing()
    {
        var (transfer, reservation) = Create(balance: 1_000_00, amount: 100_00);

        Assert.Equal(ExternalTransferStatus.Created, transfer.Status);
        Assert.NotNull(reservation);
        Assert.Contains(reservation.Entries, e => e.LedgerAccountId == SystemLedgerAccounts.Clearing && e.Direction == EntryDirection.Credit);
    }

    [Fact]
    public void Sem_saldo_e_recusada_sem_reserva()
    {
        var (transfer, reservation) = Create(balance: 10_00, amount: 100_00);

        Assert.Equal(ExternalTransferStatus.Rejected, transfer.Status);
        Assert.Null(reservation);
    }

    [Fact]
    public void Envio_grava_unknown_antes_da_chamada()
    {
        var (transfer, _) = Create(balance: 1_000_00, amount: 100_00);

        transfer.MarkSubmitting(TestCustomers.Now, CheckAfter);

        Assert.Equal(ExternalTransferStatus.Unknown, transfer.Status);
        Assert.Equal(1, transfer.SubmitAttempts);
    }

    [Fact]
    public void Resposta_incerta_nao_vira_failed()
    {
        var (transfer, reservation) = Create(balance: 1_000_00, amount: 100_00);
        transfer.MarkSubmitting(TestCustomers.Now, CheckAfter);

        var posting = transfer.ApplySubmitResult(new SubmitResult(SubmitOutcome.Unknown, "timeout"), reservation!, TestCustomers.Now, CheckAfter);

        Assert.Null(posting);
        Assert.Equal(ExternalTransferStatus.Unknown, transfer.Status);
    }

    [Fact]
    public void Conclusao_liquida_de_clearing_para_settlement()
    {
        var (transfer, reservation) = Create(balance: 1_000_00, amount: 100_00);
        transfer.MarkSubmitting(TestCustomers.Now, CheckAfter);

        var posting = transfer.ApplyProviderStatus(new ProviderStatusResult(ProviderStatus.Completed), reservation!, TestCustomers.Now, CheckAfter);

        Assert.Equal(ExternalTransferStatus.Completed, transfer.Status);
        Assert.NotNull(posting);
        Assert.Equal(transfer.ResolutionExternalId, posting.ExternalId);
        Assert.Contains(posting.Entries, e => e.LedgerAccountId == SystemLedgerAccounts.Settlement && e.Direction == EntryDirection.Credit);
    }

    [Fact]
    public void Falha_estorna_a_reserva_com_o_mesmo_id_da_liquidacao()
    {
        var (transfer, reservation) = Create(balance: 1_000_00, amount: 100_00);
        transfer.MarkSubmitting(TestCustomers.Now, CheckAfter);

        var reversal = transfer.ApplySubmitResult(new SubmitResult(SubmitOutcome.Rejected, "invalid"), reservation!, TestCustomers.Now, CheckAfter);

        Assert.Equal(ExternalTransferStatus.Failed, transfer.Status);
        Assert.NotNull(reversal);
        Assert.Equal(reservation!.Id, reversal.ReversesTransactionId);
        Assert.Equal(transfer.ResolutionExternalId, reversal.ExternalId);
    }

    [Fact]
    public void Estado_terminal_ignora_resultados_atrasados()
    {
        var (transfer, reservation) = Create(balance: 1_000_00, amount: 100_00);
        transfer.MarkSubmitting(TestCustomers.Now, CheckAfter);
        transfer.ApplyProviderStatus(new ProviderStatusResult(ProviderStatus.Completed), reservation!, TestCustomers.Now, CheckAfter);

        var late = transfer.ApplyProviderStatus(new ProviderStatusResult(ProviderStatus.Failed), reservation!, TestCustomers.Now, CheckAfter);

        Assert.Null(late);
        Assert.Equal(ExternalTransferStatus.Completed, transfer.Status);
    }

    [Fact]
    public void Cancelamento_so_antes_do_envio()
    {
        var (transfer, reservation) = Create(balance: 1_000_00, amount: 100_00);
        transfer.MarkSubmitting(TestCustomers.Now, CheckAfter);

        var error = Assert.Throws<DomainException>(() => transfer.Cancel(reservation!, TestCustomers.Now));

        Assert.Equal("not_cancellable", error.Code);
    }

    [Theory]
    [InlineData("0000000", "0001", "123")]
    [InlineData("00000000", "12345", "123")]
    [InlineData("00000000", "0001", "")]
    [InlineData("00000000", "0001", "conta com espaço")]
    public void Destino_invalido(string bank, string branch, string account)
    {
        Assert.False(ExternalDestination.TryCreate(bank, branch, account, out _));
    }

    private (ExternalTransfer Transfer, LedgerTransaction? Reservation) Create(long balance, long amount) =>
        ExternalTransfer.Create(
            _source,
            new LedgerBalance(_source.LedgerAccountId, Brl(balance), EntryDirection.Credit, AllowNegative: false),
            Brl(amount),
            Destination,
            "sub",
            "key",
            Brl(0),
            Limits,
            TestCustomers.Now);

    private static Money Brl(long minor) => Money.FromMinor(minor, Currency.Brl);
}

public sealed class ManualResolutionTests
{
    private static readonly TransferLimits Limits = new(Money.FromMinor(20_000_00, Currency.Brl), Money.FromMinor(50_000_00, Currency.Brl));

    [Fact]
    public void So_transferencia_em_revisao_aceita_resolucao()
    {
        var (transfer, _) = Create();

        var error = Assert.Throws<DomainException>(() =>
            ManualResolution.Request(transfer, ManualOutcome.Failed, "evidência", "maker", TestCustomers.Now));

        Assert.Equal("not_in_manual_review", error.Code);
    }

    [Fact]
    public void Aprovacao_por_outro_operador_estorna_e_sai_da_revisao()
    {
        var (transfer, reservation) = Create();
        transfer.MarkSubmitting(TestCustomers.Now, TimeSpan.FromSeconds(1));
        transfer.FlagForManualReview(TestCustomers.Now);
        var resolution = ManualResolution.Request(transfer, ManualOutcome.Failed, "provider confirmou", "maker", TestCustomers.Now);

        Assert.Throws<DomainException>(() => resolution.Approve("maker", transfer, reservation!, TestCustomers.Now));
        var reversal = resolution.Approve("checker", transfer, reservation!, TestCustomers.Now);

        Assert.Equal(ExternalTransferStatus.Failed, transfer.Status);
        Assert.False(transfer.RequiresManualReview);
        Assert.Equal(reservation!.Id, reversal.ReversesTransactionId);
        Assert.Equal(ManualResolutionStatus.Applied, resolution.Status);
    }

    private static (ExternalTransfer Transfer, LedgerTransaction? Reservation) Create()
    {
        var account = Account.Open(TestCustomers.Active(), 1, Currency.Brl, TestCustomers.Now).Account;
        return ExternalTransfer.Create(
            account,
            new LedgerBalance(account.LedgerAccountId, Money.FromMinor(1_000_00, Currency.Brl), EntryDirection.Credit, false),
            Money.FromMinor(100_00, Currency.Brl),
            new ExternalDestination("00000000", "0001", "12345"),
            "sub",
            "key",
            Money.FromMinor(0, Currency.Brl),
            Limits,
            TestCustomers.Now);
    }
}
