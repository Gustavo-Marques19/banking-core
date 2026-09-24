using Banking.Application.Common;

namespace Banking.Application.Audit;

/// <summary>Uma linha da trilha de auditoria. Sem PII: ids, valores e códigos.</summary>
public sealed record AuditEntry(
    Actor Actor,
    string Operation,
    string ResourceType,
    Guid ResourceId,
    string Outcome,
    IReadOnlyDictionary<string, string?> Details)
{
    public static AuditEntry Of(Actor actor, string operation, string resourceType, Guid resourceId, string outcome, params (string Key, string? Value)[] details) =>
        new(actor, operation, resourceType, resourceId, outcome, details.ToDictionary(d => d.Key, d => d.Value));
}

/// <summary>
/// Grava na mesma transação da operação. O hash encadeado é calculado pelo banco, então a aplicação não consegue forjá-lo.
/// </summary>
public interface IAuditTrail
{
    void Record(AuditEntry entry);
}

public sealed record AuditLogView(
    long Position,
    DateTimeOffset OccurredAt,
    string Actor,
    string Operation,
    string ResourceType,
    string ResourceId,
    string Outcome,
    string? TraceId,
    string? Details);

public sealed record AuditVerification(long VerifiedEntries, long? BrokenAtPosition, string? Reason)
{
    public bool IsIntact => BrokenAtPosition is null;
}

public interface IAuditQueries
{
    Task<IReadOnlyList<AuditLogView>> ListByResourceAsync(string resourceId, int limit, CancellationToken cancellationToken);

    Task<AuditVerification> VerifyChainAsync(CancellationToken cancellationToken);
}

public sealed class AuditHandler(IAuditQueries queries)
{
    public async Task<Result<IReadOnlyList<AuditLogView>>> ListAsync(Actor actor, string? resourceId, CancellationToken cancellationToken)
    {
        if (!actor.IsOperator && !actor.IsAdmin)
        {
            return Error.Forbidden("operator_required", "Só operador ou admin consulta a auditoria.");
        }

        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return Error.Validation("resource_required", "Informe resourceId.");
        }

        return Result<IReadOnlyList<AuditLogView>>.From(await queries.ListByResourceAsync(resourceId, 200, cancellationToken));
    }

    public async Task<Result<AuditVerification>> VerifyAsync(Actor actor, CancellationToken cancellationToken)
    {
        if (!actor.IsAdmin)
        {
            return Error.Forbidden("admin_required", "Só admin verifica a trilha de auditoria.");
        }

        var result = await queries.VerifyChainAsync(cancellationToken);
        if (!result.IsIntact)
        {
            BankingTelemetry.AuditChainBroken.Add(1);
        }

        return result;
    }
}
