using Banking.Domain.Common;

namespace Banking.Domain.Payments;

public sealed record TransferLimits(Money PerTransaction, Money DailyPerAccount);
