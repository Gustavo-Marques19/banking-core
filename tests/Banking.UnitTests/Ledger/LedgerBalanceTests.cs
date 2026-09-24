using Banking.Domain.Common;
using Banking.Domain.Ledger;
using Xunit;

namespace Banking.UnitTests.Ledger;

public sealed class LedgerBalanceTests
{
    [Fact]
    public void Conta_de_cliente_credito_aumenta_e_debito_diminui()
    {
        var balance = Customer(1000);

        Assert.Equal(1500, balance.BalanceAfter(EntryDirection.Credit, Brl(500)).MinorUnits);
        Assert.Equal(400, balance.BalanceAfter(EntryDirection.Debit, Brl(600)).MinorUnits);
    }

    [Fact]
    public void Conta_de_cliente_nao_pode_ficar_negativa()
    {
        var balance = Customer(1000);

        Assert.True(balance.CanPost(EntryDirection.Debit, Brl(1000)));
        Assert.False(balance.CanPost(EntryDirection.Debit, Brl(1001)));
    }

    [Fact]
    public void Conta_de_sistema_pode_ficar_negativa()
    {
        var funding = new LedgerBalance(SystemLedgerAccounts.Funding, Brl(0), EntryDirection.Debit, AllowNegative: true);

        Assert.True(funding.CanPost(EntryDirection.Credit, Brl(1_000_000)));
    }

    private static LedgerBalance Customer(long minor) =>
        new(Guid.NewGuid(), Brl(minor), EntryDirection.Credit, AllowNegative: false);

    private static Money Brl(long minor) => Money.FromMinor(minor, Currency.Brl);
}
