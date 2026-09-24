using Banking.Domain.Common;
using Xunit;

namespace Banking.UnitTests.Common;

public sealed class CurrencyTests
{
    [Fact]
    public void Brl_tem_duas_casas()
    {
        Assert.Equal(2, Currency.FromCode("BRL").MinorUnitExponent);
    }

    [Theory]
    [InlineData("brl")]
    [InlineData("USD")]
    [InlineData("")]
    [InlineData(null)]
    public void Moeda_fora_da_lista_e_rejeitada(string? code)
    {
        var error = Assert.Throws<DomainException>(() => Currency.FromCode(code));

        Assert.Equal("unsupported_currency", error.Code);
    }
}
