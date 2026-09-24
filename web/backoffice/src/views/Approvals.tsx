import { useState, type ReactNode } from "react";
import { api } from "@banking/web-shared/client";
import type { AccountView, DepositView, ExternalTransferView, ManualResolutionView, ReversalView, TransferView } from "../api/types";
import { Amount } from "../components/Amount";
import { ConfirmDialog } from "@banking/web-shared/ConfirmDialog";
import { Empty, ErrorState, Loading } from "@banking/web-shared/States";
import { currencySymbol, formatAmount, formatDateTime, shortId } from "@banking/web-shared/money";
import { useResource } from "@banking/web-shared/useResource";
import { useSession } from "../session";

/** Um pedido esperando o segundo operador, já com o que é preciso para decidir. */
interface PendingRequest {
  id: string;
  kind: "deposit" | "reversal" | "resolution";
  kindLabel: string;
  amount: string;
  currency: string;
  subject: ReactNode;
  justification: string;
  requestedBy: string;
  createdAt: string;
  approvePath: string;
  rejectPath: string;
}

async function loadQueue(): Promise<PendingRequest[]> {
  const [deposits, reversals, resolutions] = await Promise.all([
    api<DepositView[]>("/api/v1/operations/pending-deposits"),
    api<ReversalView[]>("/api/v1/operations/pending-reversals"),
    api<ManualResolutionView[]>("/api/v1/operations/pending-manual-resolutions"),
  ]);

  const depositItems = await Promise.all(
    deposits.map(async (deposit): Promise<PendingRequest> => {
      const account = await api<AccountView>(`/api/v1/accounts/${deposit.accountId}`);
      return {
        id: deposit.id,
        kind: "deposit",
        kindLabel: "Depósito acima do limite",
        amount: deposit.amount,
        currency: deposit.currency,
        subject: (
          <>
            na conta <strong>{account.branch}/{account.number}</strong>
          </>
        ),
        justification: deposit.reason,
        requestedBy: deposit.requestedBy,
        createdAt: deposit.createdAt,
        approvePath: `/api/v1/deposits/${deposit.id}/approve`,
        rejectPath: `/api/v1/deposits/${deposit.id}/reject`,
      };
    }),
  );

  const reversalItems = await Promise.all(
    reversals.map(async (reversal): Promise<PendingRequest> => {
      const transfer = await api<TransferView>(`/api/v1/transfers/${reversal.transferId}`);
      return {
        id: reversal.id,
        kind: "reversal",
        kindLabel: "Estorno de transferência",
        amount: transfer.amount,
        currency: transfer.currency,
        subject: (
          <>
            da transferência <span className="mono" title={transfer.id}>{shortId(transfer.id)}</span>, debitando quem recebeu
          </>
        ),
        justification: reversal.reason,
        requestedBy: reversal.requestedBy,
        createdAt: reversal.createdAt,
        approvePath: `/api/v1/reversals/${reversal.id}/approve`,
        rejectPath: `/api/v1/reversals/${reversal.id}/reject`,
      };
    }),
  );

  const resolutionItems = await Promise.all(
    resolutions.map(async (resolution): Promise<PendingRequest> => {
      const transfer = await api<ExternalTransferView>(`/api/v1/external-transfers/${resolution.externalTransferId}`);
      const outcome = resolution.outcome === "completed" ? "concluída (liquida)" : "falhou (estorna ao cliente)";
      return {
        id: resolution.id,
        kind: "resolution",
        kindLabel: "Resolução manual",
        amount: transfer.amount,
        currency: transfer.currency,
        subject: (
          <>
            transferência externa marcada como <strong>{outcome}</strong>
          </>
        ),
        justification: resolution.evidence,
        requestedBy: resolution.requestedBy,
        createdAt: resolution.createdAt,
        approvePath: `/api/v1/manual-resolutions/${resolution.id}/approve`,
        rejectPath: `/api/v1/manual-resolutions/${resolution.id}/reject`,
      };
    }),
  );

  return [...depositItems, ...reversalItems, ...resolutionItems].sort((a, b) => a.createdAt.localeCompare(b.createdAt));
}

interface Decision {
  request: PendingRequest;
  approve: boolean;
}

export function Approvals() {
  const session = useSession();
  const [queue, reload] = useResource(loadQueue);
  const [decision, setDecision] = useState<Decision | null>(null);
  const [announcement, setAnnouncement] = useState("");

  async function decide({ request, approve }: Decision) {
    await api(approve ? request.approvePath : request.rejectPath, { method: "POST" });
    setAnnouncement(`${request.kindLabel} ${approve ? "aprovado" : "recusado"}.`);
    reload();
  }

  return (
    <>
      <header className="page-header">
        <h1>Aprovações</h1>
        <p>
          Pedidos que um operador fez e outro precisa decidir: depósitos acima do limite, estornos e resoluções manuais de
          transferências externas. Quem pediu não aprova.
        </p>
      </header>

      <p className="visually-hidden" aria-live="polite">
        {announcement}
      </p>

      {queue.state === "loading" && <Loading what="pedidos pendentes" />}
      {queue.state === "error" && <ErrorState message={queue.message} onRetry={reload} />}
      {queue.state === "ready" && queue.data.length === 0 && (
        <Empty>
          Nenhum pedido esperando decisão. Depósitos acima do limite de aprovação, estornos e resoluções manuais aparecem aqui
          assim que alguém os registra.
        </Empty>
      )}
      {queue.state === "ready" && queue.data.length > 0 && (
        <section className="section" aria-labelledby="queue-title">
          <h2 id="queue-title" className="section__title">
            Esperando decisão <span className="section__count">{queue.data.length}</span>
          </h2>
          <ul className="queue">
            {queue.data.map((request) => {
              const mine = request.requestedBy === session.subject;
              return (
                <li key={`${request.kind}-${request.id}`} className="request">
                  <div>
                    <p className="request__kind">{request.kindLabel}</p>
                    <Amount value={request.amount} currency={request.currency} />
                  </div>
                  <div className="request__body">
                    <p>{request.subject}</p>
                    <p className="muted">“{request.justification}”</p>
                    <p className="maker-checker">
                      Pedido por <strong>{mine ? "você" : "outro operador"}</strong>{" "}
                      <span className="mono" title={request.requestedBy}>
                        ({shortId(request.requestedBy)})
                      </span>{" "}
                      em {formatDateTime(request.createdAt)} · <span className="status status--warning">pendente</span>
                    </p>
                  </div>
                  <div className="request__actions">
                    {mine ? (
                      <p className="request__blocked">Você fez este pedido. Outro operador precisa decidir.</p>
                    ) : (
                      <>
                        <button type="button" className="button button--primary" onClick={() => setDecision({ request, approve: true })}>
                          Aprovar
                        </button>
                        <button type="button" className="button button--danger" onClick={() => setDecision({ request, approve: false })}>
                          Recusar
                        </button>
                      </>
                    )}
                  </div>
                </li>
              );
            })}
          </ul>
        </section>
      )}

      {decision && (
        <ConfirmDialog
          title={`${decision.approve ? "Aprovar" : "Recusar"}: ${decision.request.kindLabel.toLowerCase()}`}
          tone={decision.approve ? "primary" : "danger"}
          confirmLabel={decision.approve ? "Confirmar aprovação" : "Confirmar recusa"}
          facts={[
            ["Valor", `${currencySymbol(decision.request.currency)} ${formatAmount(decision.request.amount)}`],
            ["O quê", decision.request.subject],
            ["Justificativa", decision.request.justification],
            ["Efeito", decision.approve ? effectOf(decision.request.kind) : "Nada é lançado; o pedido fica registrado como recusado."],
          ]}
          onConfirm={() => decide(decision)}
          onClose={() => setDecision(null)}
        />
      )}
    </>
  );
}

function effectOf(kind: PendingRequest["kind"]): string {
  switch (kind) {
    case "deposit":
      return "Lança D Funding / C conta do cliente, se a conta e o limite diário ainda permitirem.";
    case "reversal":
      return "Lança o estorno ligado à transferência original, se quem recebeu ainda tiver saldo.";
    case "resolution":
      return "Liquida ou estorna a transferência externa conforme o desfecho registrado.";
  }
}
