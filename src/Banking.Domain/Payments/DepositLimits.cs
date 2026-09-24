using Banking.Domain.Common;

namespace Banking.Domain.Payments;

/// <summary>
/// Controle sobre criação de dinheiro fictício (threat model T4). Acima de <see cref="ApprovalThreshold"/>, o depósito
/// espera a aprovação de outro operador; acima de <see cref="PerOperation"/>, é recusado.
/// </summary>
public sealed record DepositLimits(Money PerOperation, Money DailyPerOperator, Money ApprovalThreshold);
