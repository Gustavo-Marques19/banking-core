using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using Banking.Domain.Accounts;
using Banking.IntegrationTests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Banking.IntegrationTests.Api;

public sealed class ObservabilityTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Cpf_nao_aparece_em_log_nem_em_evento()
    {
        var sink = new CapturingSink();
        await using var api = new ApiFactory(
            postgres.AppConnectionString, configureServices: services => services.AddSingleton<ILogEventSink>(sink));
        var bank = new TestBank(api);
        var cpf = TestCpf.New();

        var (user, customerId) = await bank.NewCustomerAsync(TestCpf.Formatted(cpf));
        await bank.Client(user).GetAsync($"/api/v1/customers/{customerId}", Ct);
        await bank.Client(TestUser.NewCustomer()).PostAsJsonAsync("/api/v1/customers", new { name = "Outro", document = cpf }, Ct);
        await bank.Client(bank.Operator).GetAsync($"/api/v1/customers/{customerId}", Ct);

        // Loga o objeto de propósito: a política de destructuring tem que mascarar.
        api.Services.GetRequiredService<ILogger<ObservabilityTests>>()
            .LogInformation("Documento recebido {@Document}", Cpf.Parse(cpf));

        Assert.Contains(sink.Rendered, line => line.Contains($"***.{cpf[3..6]}.{cpf[6..9]}-**", StringComparison.Ordinal));
        Assert.DoesNotContain(sink.Rendered, line => line.Contains(cpf, StringComparison.Ordinal));
        Assert.DoesNotContain(sink.Rendered, line => line.Contains(TestCpf.Formatted(cpf), StringComparison.Ordinal));

        await using var connection = await Db.OpenAsync(postgres.AppConnectionString);
        Assert.Equal(0L, await Db.ScalarAsync<long>(
            connection, "SELECT count(*) FROM platform.outbox_messages WHERE payload::text LIKE @cpf", null, ("cpf", $"%{cpf}%")));
    }

    [Fact]
    public async Task Resposta_traz_trace_e_repete_o_correlation_id_do_cliente()
    {
        var client = postgres.Api.ClientFor(TestUser.NewCustomer());
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "pedido-123");

        var response = await client.GetAsync("/api/v1/accounts", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("pedido-123", response.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Matches("^[0-9a-f]{32}$", response.Headers.GetValues("X-Trace-Id").Single());
    }

    [Fact]
    public async Task Transferencia_concluida_incrementa_transfer_success()
    {
        var measurements = new ConcurrentBag<(string Kind, long Value)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "Banking" && instrument.Name == "transfer.success")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "kind")
                {
                    measurements.Add(((string)tag.Value!, value));
                }
            }
        });
        listener.Start();

        var bank = new TestBank(postgres.Api);
        var (alice, from) = await bank.NewAccountAsync("10.00");
        var (_, to) = await bank.NewAccountAsync();
        await TestBank.EnsureStatusAsync(await Transfers.SendAsync(bank, alice, from, to, "1.00"), HttpStatusCode.Created);

        Assert.Contains(measurements, m => m.Kind == "internal" && m.Value == 1);
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public ConcurrentQueue<string> Rendered { get; } = new();

        public void Emit(LogEvent logEvent)
        {
            using var writer = new StringWriter();
            logEvent.RenderMessage(writer);
            foreach (var property in logEvent.Properties)
            {
                writer.Write($" {property.Key}={property.Value}");
            }

            writer.Write(logEvent.Exception?.ToString());
            Rendered.Enqueue(writer.ToString());
        }
    }
}
