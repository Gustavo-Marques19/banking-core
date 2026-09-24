import type { ExternalTransferStatus, ExternalTransferView } from "../api/types";

export const terminal: ReadonlySet<ExternalTransferStatus> = new Set(["completed", "failed", "cancelled", "rejected"]);

export type Tone = "warning" | "success" | "danger";

/**
 * O que dizer ao cliente em cada estado (docs/estados.md). UNKNOWN é honesto sem assustar: a ordem pode ter chegado ao
 * outro banco, o valor está reservado e não sai duas vezes.
 */
export function describe(transfer: ExternalTransferView): { title: string; detail: string; tone: Tone } {
  if (transfer.requiresManualReview && !terminal.has(transfer.status)) {
    return {
      title: "Em análise pela nossa equipe",
      detail: "O outro banco não confirmou a transferência. O valor segue reservado e não será cobrado duas vezes. Avisamos aqui quando houver resposta.",
      tone: "warning",
    };
  }

  switch (transfer.status) {
    case "created":
      return { title: "Recebida, aguardando envio", detail: "O valor já está reservado. Até o envio, dá para cancelar.", tone: "warning" };
    case "unknown":
      return { title: "Enviando ao outro banco", detail: "Estamos confirmando o recebimento com o outro banco. O valor está reservado.", tone: "warning" };
    case "processing":
      return { title: "Em processamento no outro banco", detail: "O outro banco recebeu a ordem e está processando.", tone: "warning" };
    case "completed":
      return { title: "Concluída", detail: "O dinheiro chegou ao outro banco.", tone: "success" };
    case "failed":
      return { title: "Não concluída", detail: "O outro banco recusou a transferência. O valor voltou para a sua conta.", tone: "danger" };
    case "cancelled":
      return { title: "Cancelada", detail: "A transferência foi cancelada antes do envio. O valor voltou para a sua conta.", tone: "danger" };
    case "rejected":
      return { title: "Recusada", detail: "Nada saiu da sua conta.", tone: "danger" };
  }
}

/** Passos da linha do tempo, mostrada enquanto a transferência anda e quando ela conclui. */
export const steps = ["Recebida", "Enviada ao outro banco", "Concluída"] as const;

export function stepIndex(status: ExternalTransferStatus): number {
  switch (status) {
    case "created":
      return 0;
    case "unknown":
    case "processing":
      return 1;
    case "completed":
      return 2;
    default:
      return 0;
  }
}
