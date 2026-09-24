using Banking.Application.Accounts;
using Xunit;

namespace Banking.UnitTests.Application;

public sealed class AccountLookupTests
{
    [Theory]
    [InlineData("Maria", "Maria")]
    [InlineData("Maria Souza", "Maria S.")]
    [InlineData("  maria   da silva  ", "maria S.")]
    public void Titular_aparece_com_primeiro_nome_e_inicial_do_sobrenome(string name, string expected) =>
        Assert.Equal(expected, AccountLookupHandler.MaskName(name));
}
