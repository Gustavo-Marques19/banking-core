using Banking.Application.Abstractions;
using Banking.Domain.Payments;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace Banking.Infrastructure.Provider;

/// <summary>
/// Timeout e circuit breaker nas duas chamadas; retry só na consulta de status, que não tem efeito colateral.
/// O envio não é repetido aqui: quem reenvia é a reconciliação, depois de confirmar que o provider não recebeu.
/// </summary>
internal sealed class ResilientBankingProvider : IBankingProvider
{
    private readonly IBankingProvider _inner;
    private readonly ResiliencePipeline _submit;
    private readonly ResiliencePipeline _query;

    public ResilientBankingProvider(IBankingProvider inner, IOptions<ProviderOptions> options)
    {
        _inner = inner;
        var transient = new PredicateBuilder()
            .Handle<ProviderHttpException>(e => e.StatusCode >= 500)
            .Handle<TimeoutRejectedException>()
            .Handle<HttpRequestException>();

        var breaker = new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 5,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(15),
            ShouldHandle = transient,
        };

        _submit = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(breaker)
            .AddTimeout(options.Value.Timeout)
            .Build();

        _query = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = transient,
            })
            .AddCircuitBreaker(breaker)
            .AddTimeout(options.Value.Timeout)
            .Build();
    }

    public async Task<SubmitResult> SubmitTransferAsync(ProviderTransferRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await _submit.ExecuteAsync(ct => new ValueTask<SubmitResult>(_inner.SubmitTransferAsync(request, ct)), cancellationToken);
        }
        catch (Exception error) when (IsUncertain(error, cancellationToken))
        {
            // Não dá para saber se a ordem chegou: UNKNOWN, nunca FAILED (ADR-006).
            return new SubmitResult(SubmitOutcome.Unknown, Describe(error));
        }
    }

    public async Task<ProviderStatusResult> GetTransferStatusAsync(string clientReference, CancellationToken cancellationToken)
    {
        try
        {
            return await _query.ExecuteAsync(ct => new ValueTask<ProviderStatusResult>(_inner.GetTransferStatusAsync(clientReference, ct)), cancellationToken);
        }
        catch (Exception error) when (IsUncertain(error, cancellationToken))
        {
            throw new ProviderUnavailableException($"Consulta de status falhou: {Describe(error)}", error);
        }
    }

    public async Task<IReadOnlyList<ProviderStatementLine>> GetStatementAsync(DateOnly date, CancellationToken cancellationToken)
    {
        try
        {
            return await _query.ExecuteAsync(ct => new ValueTask<IReadOnlyList<ProviderStatementLine>>(_inner.GetStatementAsync(date, ct)), cancellationToken);
        }
        catch (Exception error) when (IsUncertain(error, cancellationToken))
        {
            throw new ProviderUnavailableException($"Extrato indisponível: {Describe(error)}", error);
        }
    }

    private static bool IsUncertain(Exception error, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested
        && error is TimeoutRejectedException or BrokenCircuitException or ProviderHttpException or HttpRequestException;

    private static string Describe(Exception error) => error switch
    {
        TimeoutRejectedException => "timeout",
        BrokenCircuitException => "circuit_open",
        ProviderHttpException http => $"http_{http.StatusCode}",
        _ => "network_error",
    };
}
