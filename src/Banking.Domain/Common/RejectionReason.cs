namespace Banking.Domain.Common;

/// <summary>Motivos de recusa de negócio. A operação recusada é gravada e a resposta se repete na idempotência.</summary>
public enum RejectionReason
{
    AccountBlocked,
    AccountClosed,
    InsufficientFunds,
    LimitExceeded,
    DailyLimitExceeded,
    CurrencyMismatch,
    SameAccount,

    /// <summary>Destino bloqueado ou encerrado. Não diz qual dos dois: a conta é de outra pessoa.</summary>
    DestinationUnavailable,
    SelfApprovalNotAllowed,

    /// <summary>O aprovador recusou a operação pendente.</summary>
    RejectedByApprover,
}
