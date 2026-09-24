using Banking.Domain.Accounts;
using Banking.Domain.Common;
using Xunit;

namespace Banking.UnitTests.Accounts;

public sealed class AccountTests
{
    [Fact]
    public void Abrir_conta_cria_a_conta_contabil_de_passivo()
    {
        var (account, ledgerAccount) = Account.Open(TestCustomers.Active(), 42, Currency.Brl, TestCustomers.Now);

        Assert.Equal("00000042", account.Number);
        Assert.Equal(ledgerAccount.Id, account.LedgerAccountId);
        Assert.Equal(account.Id, ledgerAccount.AccountId);
        Assert.Equal("2.1.00000042", ledgerAccount.Code);
        Assert.False(ledgerAccount.AllowNegative);
    }

    [Fact]
    public void Conta_bloqueada_recusa_movimento_e_volta_ao_desbloquear()
    {
        var (account, _) = Account.Open(TestCustomers.Active(), 1, Currency.Brl, TestCustomers.Now);

        account.Block();
        Assert.Equal(RejectionReason.AccountBlocked, account.RejectionForMovement());

        account.Unblock();
        Assert.Null(account.RejectionForMovement());
    }
}

internal static class TestCustomers
{
    public static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    public static Customer Active() =>
        Customer.Register("sub-1", "Maria", Cpf.Parse("52998224725"), new FakeProtector(), Now);

    private sealed class FakeProtector : IDocumentProtector
    {
        public ProtectedDocument Protect(Cpf cpf, Guid customerId) => new([1, 2, 3], [4, 5, 6]);

        public Cpf Unprotect(byte[] ciphertext, Guid customerId) => Cpf.Parse("52998224725");
    }
}
