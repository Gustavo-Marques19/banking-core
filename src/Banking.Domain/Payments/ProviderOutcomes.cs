namespace Banking.Domain.Payments;

/// <summary>O que o provider respondeu ao envio. Timeout, 5xx e erro de rede viram <see cref="Unknown"/> na infraestrutura.</summary>
public enum SubmitOutcome
{
    Accepted,
    AlreadyExists,
    Rejected,
    Unknown,
}

public enum ProviderStatus
{
    NotFound,
    Processing,
    Completed,
    Failed,
}

public sealed record SubmitResult(SubmitOutcome Outcome, string? Reason = null);

public sealed record ProviderStatusResult(ProviderStatus Status, string? Reason = null);
