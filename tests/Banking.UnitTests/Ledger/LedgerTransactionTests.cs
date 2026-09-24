using Banking.Domain.Common;
using Banking.Domain.Ledger;
using Xunit;

namespace Banking.UnitTests.Ledger;

public sealed class LedgerTransactionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Customer = Guid.NewGuid();

    [Fact]
    public void Transacao_balanceada_gera_um_lancamento_por_linha()
    {
        var transaction = Deposit(10000);

        Assert.Equal(2, transaction.Entries.Count);
        Assert.All(transaction.Entries, e => Assert.Equal(transaction.Id, e.TransactionId));
    }

    [Fact]
    public void Debito_sem_credito_correspondente_e_rejeitado()
    {
        var error = Assert.Throws<DomainException>(() => LedgerTransaction.Create(
            "op-1",
            LedgerTransactionType.Deposit,
            "teste",
            Now,
            [
                PostingLine.Debit(SystemLedgerAccounts.Funding, Brl(10000)),
                PostingLine.Credit(Customer, Brl(9000)),
            ]));

        Assert.Equal("unbalanced_transaction", error.Code);
    }

    [Fact]
    public void Lancamento_unico_e_rejeitado()
    {
        var error = Assert.Throws<DomainException>(() => LedgerTransaction.Create(
            "op-1", LedgerTransactionType.Deposit, "teste", Now, [PostingLine.Credit(Customer, Brl(100))]));

        Assert.Equal("unbalanced_transaction", error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Valor_zero_ou_negativo_e_rejeitado(long minor)
    {
        var error = Assert.Throws<DomainException>(() => LedgerTransaction.Create(
            "op-1",
            LedgerTransactionType.Deposit,
            "teste",
            Now,
            [PostingLine.Debit(SystemLedgerAccounts.Funding, Brl(minor)), PostingLine.Credit(Customer, Brl(minor))]));

        Assert.Equal("invalid_amount", error.Code);
    }

    [Fact]
    public void Estorno_inverte_os_lados_e_aponta_para_a_original()
    {
        var original = Deposit(10000);

        var reversal = original.Reverse("op-1:reversal", LedgerTransactionType.TransferReversal, "estorno", Now);

        Assert.Equal(original.Id, reversal.ReversesTransactionId);
        Assert.Contains(reversal.Entries, e => e.LedgerAccountId == Customer && e.Direction == EntryDirection.Debit);
        Assert.Contains(reversal.Entries, e => e.LedgerAccountId == SystemLedgerAccounts.Funding && e.Direction == EntryDirection.Credit);
    }

    [Fact]
    public void Data_contabil_vem_do_momento_do_lancamento()
    {
        var transaction = LedgerTransaction.Create(
            "op-1",
            LedgerTransactionType.Deposit,
            "teste",
            new DateTimeOffset(2026, 1, 1, 2, 0, 0, TimeSpan.Zero),
            [PostingLine.Debit(SystemLedgerAccounts.Funding, Brl(1)), PostingLine.Credit(Customer, Brl(1))]);

        Assert.Equal(new DateOnly(2025, 12, 31), transaction.EffectiveDate);
    }

    private static LedgerTransaction Deposit(long minor) => LedgerTransaction.Create(
        "op-1",
        LedgerTransactionType.Deposit,
        "teste",
        Now,
        [PostingLine.Debit(SystemLedgerAccounts.Funding, Brl(minor)), PostingLine.Credit(Customer, Brl(minor))]);

    private static Money Brl(long minor) => Money.FromMinor(minor, Currency.Brl);

    [Theory]
    [InlineData("transfer:0192a3b4-0000-7000-8000-000000000001", "0192a3b4-0000-7000-8000-000000000001")]
    [InlineData("transfer:0192a3b4-0000-7000-8000-000000000001:reversal", "0192a3b4-0000-7000-8000-000000000001")]
    [InlineData("external-transfer:0192a3b4-0000-7000-8000-000000000002:reservation", "0192a3b4-0000-7000-8000-000000000002")]
    [InlineData("deposit:0192a3b4-0000-7000-8000-000000000003", "0192a3b4-0000-7000-8000-000000000003")]
    public void Id_da_operacao_sai_do_external_id(string externalId, string expected) =>
        Assert.Equal(Guid.Parse(expected), LedgerTransaction.OperationIdOf(externalId));

    [Theory]
    [InlineData("seed")]
    [InlineData("transfer:nao-e-guid")]
    public void External_id_sem_operacao_nao_tem_codigo(string externalId) =>
        Assert.Null(LedgerTransaction.OperationIdOf(externalId));
}
