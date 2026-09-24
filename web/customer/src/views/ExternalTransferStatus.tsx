import { api } from "@banking/web-shared/client";
import { ConfirmDialog } from "@banking/web-shared/ConfirmDialog";
import { currencySymbol, formatAmount, formatDateTime } from "@banking/web-shared/money";
import { ErrorState, Loading } from "@banking/web-shared/States";
import { useEffect, useState } from "react";
import { Link, useParams } from "react-router";
import type { ExternalTransferView } from "../api/types";
import { describe, stepIndex, steps, terminal } from "../lib/externalStatus";
import { messageFor } from "../lib/messages";
import { OperationCode } from "../components/OperationCode";

export const pollInterval = 2000;

type Load = { state: "loading" } | { state: "error"; message: string } | { state: "ready"; transfer: ExternalTransferView };

/** Consulta a cada 2 s até um estado final. Erro passageiro numa consulta não apaga o último estado conhecido. */
export function ExternalTransferStatus() {
  const { id } = useParams<{ id: string }>();
  const [load, setLoad] = useState<Load>({ state: "loading" });
  const [pollError, setPollError] = useState<string | null>(null);
  const [tick, setTick] = useState(0);
  const [cancelling, setCancelling] = useState(false);

  const done = load.state === "ready" && terminal.has(load.transfer.status);
  useEffect(() => {
    if (done) {
      return;
    }

    let active = true;
    const fetchOnce = () =>
      api<ExternalTransferView>(`/api/v1/external-transfers/${id}`)
        .then(
          (transfer) => {
            if (active) {
              setLoad({ state: "ready", transfer });
              setPollError(null);
            }
          },
          (error: unknown) => {
            if (active) {
              setLoad((current) => (current.state === "ready" ? current : { state: "error", message: messageFor(error) }));
              setPollError(messageFor(error));
            }
          },
        )
        .finally(() => active && setTick((t) => t + 1));

    // A primeira consulta sai na hora; as seguintes, a cada intervalo. Depois de erro na primeira, espera o "Tentar de novo".
    if (load.state === "error") {
      return;
    }

    const timer = window.setTimeout(fetchOnce, tick === 0 ? 0 : pollInterval);
    return () => {
      active = false;
      window.clearTimeout(timer);
    };
  }, [id, tick, done, load.state]);

  if (load.state === "loading") {
    return <Loading what="a transferência" />;
  }

  if (load.state === "error") {
    return (
      <ErrorState
        message={load.message}
        onRetry={() => {
          setTick(0);
          setLoad({ state: "loading" });
        }}
      />
    );
  }

  const transfer = load.transfer;
  const view = describe(transfer);
  const current = stepIndex(transfer.status);
  const amount = `${currencySymbol(transfer.currency)} ${formatAmount(transfer.amount)}`;

  return (
    <>
      <header className="page-header">
        <h1>Transferência para outro banco</h1>
        <p>
          {amount} para o banco {transfer.destination.bank}, agência {transfer.destination.branch}, conta {transfer.destination.account}.
        </p>
      </header>

      <section className="panel tracking" aria-labelledby="tracking-status">
        <p id="tracking-status" className={`tracking__status status--${view.tone}`} role="status">
          {view.title}
        </p>
        <p>{view.detail}</p>

        {/* A linha do tempo só faz sentido enquanto anda ou quando chega ao fim; recusa e cancelamento dizem tudo no título. */}
        {(!done || transfer.status === "completed") && (
        <ol className="timeline">
          {steps.map((label, index) => {
            const state = index < current || (index === current && transfer.status === "completed") ? "done" : index === current ? "current" : "next";
            return (
              <li key={label} className={`timeline__step timeline__step--${state}`} aria-current={state === "current" ? "step" : undefined}>
                {label}
                <span className="visually-hidden">{state === "done" ? " (feito)" : state === "current" ? " (agora)" : " (a seguir)"}</span>
              </li>
            );
          })}
        </ol>
        )}

        <p>
          Código da transferência: <OperationCode code={transfer.id} />
        </p>
        <p className="muted">
          Pedida em {formatDateTime(transfer.createdAt)}. Última mudança em {formatDateTime(transfer.updatedAt)}.
          {!done && " Esta tela se atualiza sozinha."}
        </p>
        {pollError && !done && <p className="field__error">Não conseguimos atualizar agora. Tentando de novo em alguns segundos.</p>}

        <div className="form__actions">
          {transfer.status === "created" && (
            <button type="button" className="button button--danger" onClick={() => setCancelling(true)}>
              Cancelar transferência
            </button>
          )}
          <Link className="button" to="/">
            Voltar para a conta
          </Link>
        </div>
      </section>

      {cancelling && (
        <ConfirmDialog
          title="Cancelar a transferência?"
          tone="danger"
          confirmLabel="Cancelar transferência"
          facts={[
            ["Valor", amount],
            ["Efeito", "A transferência não é enviada e o valor volta para a sua conta."],
          ]}
          onConfirm={async () => {
            const updated = await api<ExternalTransferView>(`/api/v1/external-transfers/${transfer.id}/cancel`, { method: "POST" });
            setLoad({ state: "ready", transfer: updated });
          }}
          onClose={() => setCancelling(false)}
        />
      )}
    </>
  );
}
