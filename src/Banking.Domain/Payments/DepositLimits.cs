using Banking.Domain.Common;

namespace Banking.Domain.Payments;

/// <summary>Controle sobre criação de dinheiro fictício (threat model T4).</summary>
public sealed record DepositLimits(Money PerOperation, Money DailyPerOperator);
