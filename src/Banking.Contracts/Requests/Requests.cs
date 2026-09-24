namespace Banking.Contracts.Requests;

/// <summary>CPF com ou sem máscara. Nunca aparece inteiro em resposta ou log.</summary>
public sealed record RegisterCustomerRequest(string Name, string Document);

public sealed record OpenAccountRequest(string? Currency);

/// <summary>Valor como string decimal ("100.00"), nunca número JSON (ADR-002).</summary>
public sealed record DepositRequest(string Amount, string Currency, string Reason);
