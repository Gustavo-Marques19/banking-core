using Banking.Application.Idempotency;
using Xunit;

namespace Banking.UnitTests.Application;

public sealed class IdempotencyRequestTests
{
    [Fact]
    public void Mesmos_valores_normalizados_geram_o_mesmo_hash()
    {
        var a = IdempotencyRequest.Create("sub", "deposits.create", "k", Guid.Empty, 10000L, "BRL");
        var b = IdempotencyRequest.Create("sub", "deposits.create", "k", Guid.Empty, 10000L, "BRL");

        Assert.Equal(a.RequestHash, b.RequestHash);
    }

    [Fact]
    public void Valor_diferente_gera_hash_diferente()
    {
        var a = IdempotencyRequest.Create("sub", "deposits.create", "k", Guid.Empty, 10000L, "BRL");
        var b = IdempotencyRequest.Create("sub", "deposits.create", "k", Guid.Empty, 10001L, "BRL");

        Assert.NotEqual(a.RequestHash, b.RequestHash);
    }

    [Theory]
    [InlineData("01JABCDEF", true)]
    [InlineData("a_b-c", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("com espaço", false)]
    [InlineData("ç", false)]
    public void Formato_da_chave(string? key, bool valid)
    {
        Assert.Equal(valid, IdempotencyRequest.IsValidKey(key));
    }

    [Fact]
    public void Chave_acima_de_64_caracteres_e_invalida()
    {
        Assert.False(IdempotencyRequest.IsValidKey(new string('a', 65)));
    }
}
