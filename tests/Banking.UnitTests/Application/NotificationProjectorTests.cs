using System.Text.Json;
using Banking.Application.Notifications;
using Banking.Contracts.Events;
using Xunit;

namespace Banking.UnitTests.Application;

public sealed class NotificationProjectorTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Transferencia_concluida_notifica_origem_e_destino()
    {
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        var envelope = Envelope("TransferCompleted", new TransferCompletedV1(Guid.NewGuid(), source, destination, "10.00", "BRL", Guid.NewGuid()));

        var drafts = NotificationProjector.Project(envelope);

        Assert.Contains(drafts, d => d.AccountId == source && d.Kind == "transfer_sent");
        Assert.Contains(drafts, d => d.AccountId == destination && d.Kind == "transfer_received");
    }

    [Fact]
    public void Evento_desconhecido_nao_gera_notificacao()
    {
        Assert.Empty(NotificationProjector.Project(Envelope("CustomerRegistered", new CustomerRegisteredV1(Guid.NewGuid()))));
    }

    private static EventEnvelope Envelope(string type, object data) =>
        new(Guid.NewGuid(), type, 1, DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(data, data.GetType(), Json));
}
