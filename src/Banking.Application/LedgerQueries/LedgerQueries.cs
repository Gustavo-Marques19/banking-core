using Banking.Application.Abstractions;
using Banking.Application.Common;
using Banking.Domain.Ledger;

namespace Banking.Application.LedgerQueries;

public sealed record LedgerEntryView(Guid LedgerAccountId, string Direction, string Amount, string Currency, long? AccountSequence, string? BalanceAfter);

public sealed record LedgerTransactionView(
    Guid Id,
    string ExternalId,
    string Type,
    string Description,
    DateTimeOffset PostedAt,
    DateOnly EffectiveDate,
    Guid? ReversesTransactionId,
    IReadOnlyList<LedgerEntryView> Entries);

public sealed class GetLedgerTransactionHandler(ILedger ledger)
{
    public async Task<Result<LedgerTransactionView>> HandleAsync(Actor actor, Guid transactionId, CancellationToken cancellationToken)
    {
        if (!actor.IsOperator)
        {
            return Error.NotFound("Transação contábil");
        }

        var transaction = await ledger.FindTransactionAsync(transactionId, cancellationToken);
        if (transaction is null)
        {
            return Error.NotFound("Transação contábil");
        }

        return new LedgerTransactionView(
            transaction.Id,
            transaction.ExternalId,
            Codes.Of(transaction.Type),
            transaction.Description,
            transaction.PostedAt,
            transaction.EffectiveDate,
            transaction.ReversesTransactionId,
            [.. transaction.Entries.Select(e => new LedgerEntryView(
                e.LedgerAccountId,
                e.Direction == EntryDirection.Debit ? "debit" : "credit",
                e.Amount.ToDecimalString(),
                e.CurrencyCode,
                e.AccountSequence,
                e.BalanceAfterMinor is { } after ? Domain.Common.Money.FromMinor(after, e.Amount.Currency).ToDecimalString() : null))]);
    }
}
