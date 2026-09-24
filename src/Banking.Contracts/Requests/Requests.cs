namespace Banking.Contracts.Requests;

// Parâmetros sem valor padrão são obrigatórios no JSON (ContractJson). Opcional precisa de "= null".

/// <summary>CPF com ou sem máscara. Nunca aparece inteiro em resposta ou log.</summary>
public sealed record RegisterCustomerRequest(string Name, string Document);

public sealed record OpenAccountRequest(string? Currency = null);

/// <summary>Valor como string decimal ("100.00"), nunca número JSON (ADR-002).</summary>
public sealed record DepositRequest(string Amount, string Currency, string Reason);

public sealed record CreateTransferRequest(
    Guid SourceAccountId, Guid DestinationAccountId, string Amount, string Currency, string? Description = null);

public sealed record ExternalDestinationRequest(string Bank, string Branch, string Account);

public sealed record CreateExternalTransferRequest(
    Guid SourceAccountId, string Amount, string Currency, ExternalDestinationRequest Destination);

/// <summary>Webhook do provider. Só dispara uma consulta de status; o conteúdo não move dinheiro (ADR-006).</summary>
public sealed record ProviderWebhookRequest(Guid EventId, string ClientReference, string Status);

public sealed record ProviderScenarioRequest(string Scenario);
