using Banking.Application.Common;
using Banking.Domain.Accounts;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Banking.Api.Composition;

internal static class ObservabilitySetup
{
    public const string ServiceName = "banking-api";

    /// <summary>
    /// Traces e métricas via OpenTelemetry; logs via Serilog. Sem OTEL_EXPORTER_OTLP_ENDPOINT, nada é exportado
    /// (testes, execução local sem dashboard).
    /// </summary>
    public static WebApplicationBuilder AddBankingObservability(this WebApplicationBuilder builder)
    {
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var exportOtlp = !string.IsNullOrWhiteSpace(otlpEndpoint);

        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options => options.Filter = http =>
                    !http.Request.Path.StartsWithSegments("/health") && !http.Request.Path.StartsWithSegments("/ready"))
                .AddHttpClientInstrumentation()
                .AddNpgsql()
                .AddSource(BankingTelemetry.Name, "RabbitMQ.Client.*"))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter(BankingTelemetry.Name));

        if (exportOtlp)
        {
            telemetry.UseOtlpExporter();
        }

        builder.Services.AddSerilog((services, logger) =>
        {
            logger
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("service", ServiceName)
                .Destructure.With<PiiDestructuringPolicy>()
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");

            if (exportOtlp)
            {
                logger.WriteTo.OpenTelemetry(options =>
                {
                    options.Endpoint = otlpEndpoint;
                    options.ResourceAttributes = new Dictionary<string, object> { ["service.name"] = ServiceName };
                });
            }
        });

        return builder;
    }
}

/// <summary>CPF em log sai mascarado, mesmo se alguém logar o objeto inteiro (threat model T11).</summary>
internal sealed class PiiDestructuringPolicy : IDestructuringPolicy
{
    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, out LogEventPropertyValue result)
    {
        if (value is Cpf cpf)
        {
            result = new ScalarValue(cpf.Masked);
            return true;
        }

        result = null!;
        return false;
    }
}
