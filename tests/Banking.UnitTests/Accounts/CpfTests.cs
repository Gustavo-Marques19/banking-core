using Banking.Domain.Accounts;
using Xunit;

namespace Banking.UnitTests.Accounts;

public sealed class CpfTests
{
    [Theory]
    [InlineData("529.982.247-25")]
    [InlineData("52998224725")]
    public void Aceita_cpf_valido_com_ou_sem_mascara(string text)
    {
        Assert.Equal("52998224725", Cpf.Parse(text).Digits);
    }

    [Theory]
    [InlineData("529.982.247-24")]
    [InlineData("11111111111")]
    [InlineData("5299822472")]
    [InlineData("529982247250")]
    [InlineData("529 982 247 25")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejeita_cpf_invalido(string? text)
    {
        Assert.False(Cpf.TryParse(text, out _));
    }

    [Fact]
    public void ToString_nunca_mostra_o_cpf_inteiro()
    {
        var cpf = Cpf.Parse("52998224725");

        Assert.Equal("***.982.247-**", cpf.ToString());
        Assert.Equal("***.982.247-**", $"{cpf}");
    }
}
