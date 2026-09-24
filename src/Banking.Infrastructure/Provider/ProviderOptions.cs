namespace Banking.Infrastructure.Provider;

public sealed class ProviderOptions
{
    public const string SectionName = "Provider";

    /// <summary>Na Fase 1 só existe o mock. A Fase 2 acrescenta o provider real atrás da mesma interface.</summary>
    public string Mode { get; set; } = "Mock";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Segredo HMAC do webhook. Sem ele, o endpoint de webhook responde 503.</summary>
    public string? WebhookSecret { get; set; }

    public bool IsMock => string.Equals(Mode, "Mock", StringComparison.OrdinalIgnoreCase);
}

public sealed class ExternalTransferWorkerOptions
{
    public const string SectionName = "ExternalTransfers";

    public bool WorkerEnabled { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    public int BatchSize { get; set; } = 20;
}
