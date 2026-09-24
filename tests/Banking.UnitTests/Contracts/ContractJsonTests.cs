using System.Text.Json;
using Banking.Contracts;
using Banking.Contracts.Requests;
using Xunit;

namespace Banking.UnitTests.Contracts;

public sealed class ContractJsonTests
{
    private static readonly JsonSerializerOptions Options = ContractJson.Apply(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Fact]
    public void Transferencia_sem_descricao_e_aceita()
    {
        var request = JsonSerializer.Deserialize<CreateTransferRequest>(
            $$"""{"sourceAccountId":"{{Guid.NewGuid()}}","destinationAccountId":"{{Guid.NewGuid()}}","amount":"10.00","currency":"BRL"}""",
            Options);

        Assert.Null(request!.Description);
    }

    [Fact]
    public void Abrir_conta_sem_moeda_e_aceito()
    {
        Assert.Null(JsonSerializer.Deserialize<OpenAccountRequest>("{}", Options)!.Currency);
    }

    [Theory]
    [InlineData("""{"amount":"10.00","currency":"BRL"}""")]
    [InlineData("""{"amount":"10.00","currency":"BRL","reason":"x","extra":1}""")]
    [InlineData("""{"amount":10.00,"currency":"BRL","reason":"x"}""")]
    [InlineData("""{"amount":null,"currency":"BRL","reason":"x"}""")]
    public void Deposito_com_campo_faltando_sobrando_ou_de_tipo_errado_e_recusado(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DepositRequest>(json, Options));
    }
}
