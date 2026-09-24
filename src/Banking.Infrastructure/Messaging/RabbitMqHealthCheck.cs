using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Banking.Infrastructure.Messaging;

/// <summary>Broker fora deixa a aplicação degradada, não fora do ar: operações seguem e eventos esperam no outbox.</summary>
internal sealed class RabbitMqHealthCheck(RabbitMqConnectionProvider connections) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!connections.Options.IsConfigured)
        {
            return HealthCheckResult.Healthy("Mensageria não configurada.");
        }

        try
        {
            var connection = await connections.ConnectAsync("banking-health", cancellationToken);
            await RabbitMqConnectionProvider.AbortQuietlyAsync(connection);
            return HealthCheckResult.Healthy();
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Broker indisponível; eventos acumulam no outbox.", error);
        }
    }
}
