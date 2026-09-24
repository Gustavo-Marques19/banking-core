using Banking.Domain.Common;
using Xunit;

namespace Banking.UnitTests.Common;

public sealed class MoneyTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("0.01", 1)]
    [InlineData("1", 100)]
    [InlineData("100.5", 10050)]
    [InlineData("100.50", 10050)]
    [InlineData("999999999999999.99", 99999999999999999)]
    public void Parse_aceita_formato_canonico(string text, long expectedMinor)
    {
        var money = Money.Parse(text, Currency.Brl);

        Assert.Equal(expectedMinor, money.MinorUnits);
    }

    [Theory]
    [InlineData("100.001")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1e3")]
    [InlineData("1,00")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("01")]
    [InlineData("1.")]
    [InlineData(".5")]
    [InlineData("1\n")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1000000000000000")]
    public void Parse_rejeita_sem_arredondar(string? text)
    {
        Assert.False(Money.TryParse(text, Currency.Brl, out _));

        var error = Assert.Throws<DomainException>(() => Money.Parse(text, Currency.Brl));
        Assert.Equal("invalid_amount", error.Code);
    }

    [Theory]
    [InlineData(0, "0.00")]
    [InlineData(5, "0.05")]
    [InlineData(10050, "100.50")]
    [InlineData(-510, "-5.10")]
    public void Formata_sempre_com_as_casas_da_moeda(long minor, string expected)
    {
        Assert.Equal(expected, Money.FromMinor(minor, Currency.Brl).ToDecimalString());
    }

    [Fact]
    public void Soma_e_subtrai_na_mesma_moeda()
    {
        var a = Money.FromMinor(1000, Currency.Brl);
        var b = Money.FromMinor(250, Currency.Brl);

        Assert.Equal(Money.FromMinor(1250, Currency.Brl), a + b);
        Assert.Equal(Money.FromMinor(750, Currency.Brl), a - b);
        Assert.True(a > b);
    }

    [Fact]
    public void Overflow_lanca_excecao_em_vez_de_dar_a_volta()
    {
        var max = Money.FromMinor(long.MaxValue, Currency.Brl);

        Assert.Throws<OverflowException>(() => max + Money.FromMinor(1, Currency.Brl));
    }

    [Fact]
    public void Nao_existe_money_sem_moeda()
    {
        Assert.Throws<ArgumentNullException>(() => Money.FromMinor(1, null!));
    }
}
