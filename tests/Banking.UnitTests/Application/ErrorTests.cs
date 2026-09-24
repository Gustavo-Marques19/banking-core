using Banking.Application.Common;
using Xunit;

namespace Banking.UnitTests.Application;

public sealed class ErrorTests
{
    [Theory]
    [InlineData("Conta", "Conta inexistente.")]
    [InlineData("Depósito", "Depósito inexistente.")]
    public void Recurso_ausente_tem_mensagem_que_serve_aos_dois_generos(string resource, string expected) =>
        Assert.Equal(expected, Error.NotFound(resource).Message);
}
