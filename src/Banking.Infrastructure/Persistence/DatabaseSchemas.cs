namespace Banking.Infrastructure.Persistence;

/// <summary>Um schema por módulo (ADR-001). Cada módulo só grava nas próprias tabelas.</summary>
public static class DatabaseSchemas
{
    public const string Accounts = "accounts";
    public const string Ledger = "ledger";
    public const string Payments = "payments";
    public const string Platform = "platform";
}
