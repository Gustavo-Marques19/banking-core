import { ApiError } from "@banking/web-shared/client";

// Texto para o cliente a partir do código estável da API. O título técnico da API fica como último recurso.
const byCode: Record<string, string> = {
  insufficient_funds: "Saldo insuficiente para esse valor.",
  limit_exceeded: "O valor passa do limite por transferência.",
  daily_limit_exceeded: "Esse valor passa do seu limite diário de transferências.",
  account_blocked: "Sua conta está bloqueada para movimentação. Fale com o banco.",
  account_closed: "Essa conta está encerrada.",
  destination_unavailable: "A conta de destino não pode receber agora.",
  same_account: "A conta de destino é a mesma de origem.",
  currency_mismatch: "As duas contas precisam estar na mesma moeda.",
  invalid_amount: "Confira o valor.",
  invalid_destination: "Confira o banco, a agência e a conta de destino.",
  invalid_account_number: "Informe a agência com 4 dígitos e a conta com até 8.",
  invalid_document: "CPF inválido. Confira os números.",
  invalid_name: "Informe o nome completo.",
  customer_registration_conflict: "Não foi possível concluir o cadastro com esses dados.",
  customer_not_registered: "Termine o cadastro antes de abrir a conta.",
  not_cancellable: "Essa transferência já foi enviada e não pode mais ser cancelada.",
  rate_limited: "Muitas tentativas seguidas. Espere um minuto e tente de novo.",
  contention: "O banco está ocupado agora. Tente de novo: a operação não será feita duas vezes.",
};

export function messageFor(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.code && byCode[error.code]) {
      return byCode[error.code]!;
    }

    if (error.status === 404) {
      return "Não encontramos o que você procurou.";
    }

    if (error.status >= 500) {
      return "Algo falhou do nosso lado. Tente de novo em instantes.";
    }

    return error.message;
  }

  return "Sem conexão com o banco. Confira a internet e tente de novo.";
}

/** Uma linha por aviso. O código de recusa vem depois de ":" (transfer_rejected:insufficient_funds). */
export function notificationText(kind: string): string {
  const [base, reason] = kind.split(":");
  switch (base) {
    case "deposit_received":
      return "Depósito recebido";
    case "transfer_sent":
      return "Transferência enviada";
    case "transfer_received":
      return "Transferência recebida";
    case "transfer_rejected":
      return `Transferência recusada${reason && byCode[reason] ? `: ${byCode[reason]!.replace(/\.$/, "").toLowerCase()}` : ""}`;
    case "external_transfer_completed":
      return "Transferência para outro banco concluída";
    case "external_transfer_failed":
      return "Transferência para outro banco não concluída. O valor voltou para a sua conta";
    case "external_transfer_cancelled":
      return "Transferência para outro banco cancelada. O valor voltou para a sua conta";
    case "transfer_reversal_received":
      return "Estorno de transferência devolvido à sua conta";
    case "transfer_reversal_debited":
      return "Estorno de transferência debitado da sua conta";
    case "account_blocked":
      return "Sua conta foi bloqueada para movimentação";
    case "account_active":
      return "Sua conta foi desbloqueada";
    default:
      return "Movimentação na conta";
  }
}
