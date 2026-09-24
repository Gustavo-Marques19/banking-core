namespace Banking.Contracts.Requests;

// Parâmetros sem valor padrão são obrigatórios no JSON (ContractJson). Opcional precisa de "= null".

/// <summary>CPF com ou sem máscara. Nunca aparece inteiro em resposta ou log.</summary>
public sealed record RegisterCustomerRequest(string Name, string Document);

public sealed record OpenAccountRequest(string? Currency = null);

/// <summary>Valor como string decimal ("100.00"), nunca número JSON (ADR-002).</summary>
public sealed record DepositRequest(string Amount, string Currency, string Reason);

public sealed record CreateTransferRequest(
    Guid SourceAccountId, Guid DestinationAccountId, string Amount, string Currency, string? Description = null);
