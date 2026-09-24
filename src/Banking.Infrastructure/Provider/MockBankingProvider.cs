using System.Collections.Concurrent;
using Banking.Application.Abstractions;
using Banking.Domain.Common;
using Banking.Domain.Payments;
using Microsoft.Extensions.Options;

namespace Banking.Infrastructure.Provider;

public sealed class ProviderHttpException(int statusCode) : Exception($"Provider respondeu HTTP {statusCode}.")
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// Provider simulado, só para desenvolvimento e testes. O cenário vem da conta de destino (ex.: "TIMEOUT-123"),
/// como nos sandboxes reais, então testes paralelos não interferem entre si. Deduplica por referência.
/// </summary>
public sealed class MockBankingProvider(IOptions<ProviderOptions> options, TimeProvider time) : IBankingProvider
{
    public static readonly IReadOnlyList<string> Scenarios = ["SUCCESS", "FAIL", "TIMEOUT", "LATE", "HTTP500", "DUPLICATE", "UNKNOWN"];

    private readonly ConcurrentDictionary<string, MockTransfer> _transfers = new();
    private readonly ConcurrentDictionary<string, int> _submitCalls = new();

    private sealed record MockTransfer(Money Amount, ProviderStatus Status, string? Reason, DateTimeOffset SettledAt);

    /// <summary>Cenário para destinos sem prefixo. Alterado pelo endpoint admin em desenvolvimento.</summary>
    public string DefaultScenario { get; set; } = "SUCCESS";

    public async Task<SubmitResult> SubmitTransferAsync(ProviderTransferRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_transfers.ContainsKey(request.ClientReference))
        {
            return new SubmitResult(SubmitOutcome.AlreadyExists);
        }

        var calls = _submitCalls.AddOrUpdate(request.ClientReference, 1, (_, previous) => previous + 1);
        switch (ScenarioFor(request.Destination.Account))
        {
            case "FAIL":
                return new SubmitResult(SubmitOutcome.Rejected, "destination_account_invalid");
            case "TIMEOUT":
                await HangAsync(cancellationToken);
                return new SubmitResult(SubmitOutcome.Accepted);
            case "LATE":
                Record(request, ProviderStatus.Completed);
                await HangAsync(cancellationToken);
                return new SubmitResult(SubmitOutcome.Accepted);
            case "HTTP500" when calls == 1:
                throw new ProviderHttpException(500);
            case "DUPLICATE":
                Record(request, ProviderStatus.Completed);
                return new SubmitResult(SubmitOutcome.AlreadyExists);
            case "UNKNOWN":
                Record(request, ProviderStatus.Failed, "rejected_by_destination_bank");
                return new SubmitResult(SubmitOutcome.Unknown, "unrecognized_response");
            default:
                Record(request, ProviderStatus.Completed);
                return new SubmitResult(SubmitOutcome.Accepted);
        }
    }

    public Task<ProviderStatusResult> GetTransferStatusAsync(string clientReference, CancellationToken cancellationToken) =>
        Task.FromResult(_transfers.TryGetValue(clientReference, out var transfer)
            ? new ProviderStatusResult(transfer.Status, transfer.Reason)
            : new ProviderStatusResult(ProviderStatus.NotFound));

    public Task<IReadOnlyList<ProviderStatementLine>> GetStatementAsync(DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProviderStatementLine>>(
        [
            .. _transfers
                .Where(t => t.Value.Status == ProviderStatus.Completed && BusinessCalendar.DateOf(t.Value.SettledAt) == date)
                .Select(t => new ProviderStatementLine(t.Key, t.Value.Amount, t.Value.SettledAt)),
        ]);

    /// <summary>Liquidação que só o provider conhece. Serve para demonstrar a reconciliação apontando divergência.</summary>
    public void RecordUnmatchedSettlement(string clientReference, Money amount) =>
        _transfers[clientReference] = new MockTransfer(amount, ProviderStatus.Completed, null, time.GetUtcNow());

    private string ScenarioFor(string destinationAccount)
    {
        var prefix = destinationAccount.Split('-')[0].ToUpperInvariant();
        return Scenarios.Contains(prefix) ? prefix : DefaultScenario;
    }

    private void Record(ProviderTransferRequest request, ProviderStatus status, string? reason = null) =>
        _transfers.TryAdd(request.ClientReference, new MockTransfer(request.Amount, status, reason, time.GetUtcNow()));

    /// <summary>Demora mais que o timeout do cliente; quem chama desiste antes.</summary>
    private Task HangAsync(CancellationToken cancellationToken) =>
        Task.Delay(options.Value.Timeout * 3, time, cancellationToken);
}
