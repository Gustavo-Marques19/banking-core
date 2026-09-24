// Espelham os records de Banking.Application. Escritos à mão: as respostas da API não têm schema no OpenAPI.
// Valores monetários chegam como string decimal ("10000.00") e continuam string (ADR-002).

export interface BffUser {
  name: string | null;
  subject: string;
  roles: string[];
}

export interface DepositView {
  id: string;
  accountId: string;
  amount: string;
  currency: string;
  reason: string;
  status: string;
  rejectionReason: string | null;
  requestedBy: string;
  decidedBy: string | null;
  createdAt: string;
}

export interface ReversalView {
  id: string;
  transferId: string;
  reason: string;
  status: string;
  rejectionReason: string | null;
  requestedBy: string;
  decidedBy: string | null;
  ledgerTransactionId: string | null;
  createdAt: string;
}

export interface TransferView {
  id: string;
  sourceAccountId: string;
  destinationAccountId: string;
  amount: string;
  currency: string;
  description: string | null;
  status: string;
}

export interface ExternalTransferView {
  id: string;
  sourceAccountId: string;
  amount: string;
  currency: string;
  destination: { bank: string; branch: string; account: string };
  status: string;
  failureReason: string | null;
  requiresManualReview: boolean;
  submitAttempts: number;
  createdAt: string;
  updatedAt: string;
}

export interface ManualResolutionView {
  id: string;
  externalTransferId: string;
  outcome: "completed" | "failed";
  evidence: string;
  status: string;
  requestedBy: string;
  decidedBy: string | null;
  createdAt: string;
}

export interface AccountView {
  id: string;
  branch: string;
  number: string;
  status: string;
}

export interface ReconciliationReport {
  ranAt: string;
  statementDate: string;
  findings: { check: string; detail: string }[];
  isConsistent: boolean;
}

/** Só o que a auditoria usa do lançamento contábil. */
export interface LedgerTransactionView {
  id: string;
  externalId: string;
}

export interface AuditLogView {
  position: number;
  occurredAt: string;
  actor: string;
  operation: string;
  resourceType: string;
  resourceId: string;
  outcome: string;
  traceId: string | null;
  details: string | null;
}

export interface AuditVerification {
  verifiedEntries: number;
  brokenAtPosition: number | null;
  reason: string | null;
  isIntact: boolean;
}
