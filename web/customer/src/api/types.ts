// Espelham os records de Banking.Application. Escritos à mão: as respostas da API não têm schema no OpenAPI.
// Valores monetários chegam como string decimal ("10000.00") e continuam string (ADR-002).

export interface BffUser {
  name: string | null;
  subject: string;
  roles: string[];
}

export interface CustomerView {
  id: string;
  name: string;
  document: string;
  status: string;
}

export interface AccountView {
  id: string;
  customerId: string;
  branch: string;
  number: string;
  currency: string;
  status: string;
}

export interface BalanceView {
  accountId: string;
  ledgerBalance: string;
  availableBalance: string;
  currency: string;
  asOf: string;
}

export interface StatementLine {
  transactionId: string;
  /** Código da operação (transferência, depósito...): o mesmo do comprovante e da auditoria. */
  operationId: string | null;
  type: string;
  description: string;
  direction: "debit" | "credit";
  amount: string;
  balanceAfter: string;
  sequence: number;
  postedAt: string;
}

export interface StatementView {
  accountId: string;
  currency: string;
  lines: StatementLine[];
  nextCursor: number | null;
}

export interface NotificationView {
  id: string;
  kind: string;
  amount: string | null;
  currency: string | null;
  referenceId: string;
  createdAt: string;
}

export interface AccountLookupView {
  id: string;
  branch: string;
  number: string;
  currency: string;
  holderName: string;
}

export interface TransferView {
  id: string;
  sourceAccountId: string;
  destinationAccountId: string;
  amount: string;
  currency: string;
  description: string | null;
  status: string;
  rejectionReason: string | null;
  createdAt: string;
}

export type ExternalTransferStatus = "created" | "unknown" | "processing" | "completed" | "failed" | "cancelled" | "rejected";

export interface ExternalTransferView {
  id: string;
  sourceAccountId: string;
  amount: string;
  currency: string;
  destination: { bank: string; branch: string; account: string };
  status: ExternalTransferStatus;
  rejectionReason: string | null;
  failureReason: string | null;
  requiresManualReview: boolean;
  createdAt: string;
  updatedAt: string;
}
