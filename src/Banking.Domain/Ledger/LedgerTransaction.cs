using Banking.Domain.Common;

namespace Banking.Domain.Ledger;

/// <summary>
/// Transação contábil imutável. Não tem status: existe e está lançada, ou não existe (ADR-003).
/// </summary>
public sealed class LedgerTransaction
{
    private const int MaxDescriptionLength = 200;

    private readonly List<LedgerEntry> _entries = [];

    private LedgerTransaction()
    {
        ExternalId = string.Empty;
        Description = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>Id da operação que originou o lançamento. UNIQUE no banco: impede lançar duas vezes.</summary>
    public string ExternalId { get; private set; }

    public LedgerTransactionType Type { get; private set; }

    public string Description { get; private set; }

    public DateTimeOffset PostedAt { get; private set; }

    public DateOnly EffectiveDate { get; private set; }

    public Guid? ReversesTransactionId { get; private set; }

    public string? CorrelationId { get; private set; }

    public IReadOnlyList<LedgerEntry> Entries => _entries;

    /// <summary>
    /// Id da operação dentro do ExternalId ("transfer:{id}", "external-transfer:{id}:reservation"...). É o id que a
    /// auditoria registra e o código que o cliente vê no comprovante.
    /// </summary>
    public static Guid? OperationIdOf(string externalId)
    {
        ArgumentNullException.ThrowIfNull(externalId);
        var parts = externalId.Split(':');
        return parts.Length >= 2 && Guid.TryParse(parts[1], out var id) ? id : null;
    }

    public static LedgerTransaction Create(
        string externalId,
        LedgerTransactionType type,
        string description,
        DateTimeOffset postedAt,
        IReadOnlyCollection<PostingLine> lines,
        string? correlationId = null,
        Guid? reversesTransactionId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(lines);

        if (description.Length > MaxDescriptionLength)
        {
            throw new DomainException("invalid_description", $"Descrição acima de {MaxDescriptionLength} caracteres.");
        }

        EnsureBalanced(lines);

        var transaction = new LedgerTransaction
        {
            Id = Guid.CreateVersion7(postedAt),
            ExternalId = externalId,
            Type = type,
            Description = description,
            PostedAt = postedAt,
            EffectiveDate = BusinessCalendar.DateOf(postedAt),
            ReversesTransactionId = reversesTransactionId,
            CorrelationId = correlationId,
        };

        foreach (var line in lines)
        {
            transaction._entries.Add(new LedgerEntry(Guid.CreateVersion7(postedAt), transaction.Id, line));
        }

        return transaction;
    }

    /// <summary>Estorno: os mesmos lançamentos com os lados invertidos, apontando para esta transação.</summary>
    public LedgerTransaction Reverse(
        string externalId, LedgerTransactionType type, string description, DateTimeOffset postedAt, string? correlationId = null)
    {
        var mirrored = _entries
            .Select(e => new PostingLine(
                e.LedgerAccountId,
                e.Direction == EntryDirection.Debit ? EntryDirection.Credit : EntryDirection.Debit,
                e.Amount))
            .ToArray();

        return Create(externalId, type, description, postedAt, mirrored, correlationId, reversesTransactionId: Id);
    }

    private static void EnsureBalanced(IReadOnlyCollection<PostingLine> lines)
    {
        if (lines.Count < 2)
        {
            throw new DomainException("unbalanced_transaction", "Uma transação contábil precisa de pelo menos dois lançamentos.");
        }

        if (lines.Any(l => !l.Amount.IsPositive))
        {
            throw new DomainException("invalid_amount", "Todo lançamento precisa ter valor positivo.");
        }

        foreach (var byCurrency in lines.GroupBy(l => l.Amount.Currency))
        {
            var debits = byCurrency.Where(l => l.Direction == EntryDirection.Debit)
                .Aggregate(Money.Zero(byCurrency.Key), (sum, l) => sum + l.Amount);
            var credits = byCurrency.Where(l => l.Direction == EntryDirection.Credit)
                .Aggregate(Money.Zero(byCurrency.Key), (sum, l) => sum + l.Amount);

            if (debits != credits)
            {
                throw new DomainException(
                    "unbalanced_transaction",
                    $"Débitos ({debits}) diferentes dos créditos ({credits}).");
            }
        }
    }
}
