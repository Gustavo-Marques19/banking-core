import { useState, type FormEvent } from "react";
import { Link } from "react-router";
import { api } from "../api/client";
import type { ExternalTransferView } from "../api/types";
import { Amount } from "../components/Amount";
import { Empty, ErrorState, Loading } from "../components/States";
import { formatDateTime, shortId } from "../lib/money";
import { useResource } from "../lib/useResource";

export function ManualReview() {
  const [transfers, reload] = useResource(() => api<ExternalTransferView[]>("/api/v1/operations/external-transfers-in-review"));
  const [open, setOpen] = useState<string | null>(null);
  const [registered, setRegistered] = useState<string | null>(null);

  return (
    <>
      <header className="page-header">
        <h1>Revisão manual</h1>
        <p>
          Transferências externas em que o provider não deu resposta conclusiva depois de todas as tentativas. O dinheiro está
          reservado em Clearing. Confirme o desfecho com o provider por outro canal e registre aqui; outro operador aprova.
        </p>
      </header>

      {registered && (
        <p className="state" role="status">
          Desfecho registrado para a transferência <span className="mono">{shortId(registered)}</span>. Ele aparece em{" "}
          <Link to="/">Aprovações</Link> para outro operador decidir.
        </p>
      )}

      {transfers.state === "loading" && <Loading what="transferências em revisão" />}
      {transfers.state === "error" && <ErrorState message={transfers.message} onRetry={reload} />}
      {transfers.state === "ready" && transfers.data.length === 0 && (
        <Empty>Nenhuma transferência externa esperando revisão. Elas chegam aqui quando esgotam as consultas automáticas ao provider.</Empty>
      )}
      {transfers.state === "ready" && transfers.data.length > 0 && (
        <ul className="queue">
          {transfers.data.map((transfer) => (
            <li key={transfer.id} className={open === transfer.id ? "request request--editing" : "request"}>
              <div>
                <p className="request__kind">Transferência externa</p>
                <Amount value={transfer.amount} currency={transfer.currency} />
              </div>
              <div className="request__body">
                <p>
                  Para banco <span className="mono">{transfer.destination.bank}</span>, agência {transfer.destination.branch}, conta{" "}
                  {transfer.destination.account}
                </p>
                <p className="maker-checker">
                  {transfer.submitAttempts} envios sem resposta conclusiva · última tentativa em {formatDateTime(transfer.updatedAt)} ·{" "}
                  <span className="status status--warning">desfecho desconhecido</span>
                </p>
              </div>
              {open === transfer.id ? (
                <ResolutionForm
                  transfer={transfer}
                  onCancel={() => setOpen(null)}
                  onRegistered={() => {
                    setOpen(null);
                    setRegistered(transfer.id);
                    reload();
                  }}
                />
              ) : (
                <div className="request__actions">
                  <button type="button" className="button button--primary" onClick={() => setOpen(transfer.id)}>
                    Registrar desfecho
                  </button>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
    </>
  );
}

function ResolutionForm({
  transfer,
  onCancel,
  onRegistered,
}: {
  transfer: ExternalTransferView;
  onCancel: () => void;
  onRegistered: () => void;
}) {
  const [outcome, setOutcome] = useState<"completed" | "failed" | "">("");
  const [evidence, setEvidence] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!outcome || evidence.trim().length === 0) {
      setError("Escolha o desfecho e descreva a evidência.");
      return;
    }

    setBusy(true);
    setError(null);
    try {
      await api(`/api/v1/external-transfers/${transfer.id}/manual-resolutions`, { method: "POST", body: { outcome, evidence } });
      onRegistered();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure));
      setBusy(false);
    }
  }

  const evidenceId = `evidence-${transfer.id}`;
  return (
    <form className="form" onSubmit={submit} aria-label="Registrar desfecho">
      <fieldset className="field choices">
        <legend>O que o provider confirmou?</legend>
        <label>
          <input type="radio" name={`outcome-${transfer.id}`} value="completed" checked={outcome === "completed"} onChange={() => setOutcome("completed")} />
          Concluída: o dinheiro saiu (liquida)
        </label>
        <label>
          <input type="radio" name={`outcome-${transfer.id}`} value="failed" checked={outcome === "failed"} onChange={() => setOutcome("failed")} />
          Falhou: o dinheiro não saiu (estorna ao cliente)
        </label>
      </fieldset>
      <div className="field">
        <label htmlFor={evidenceId}>Evidência</label>
        <textarea
          id={evidenceId}
          maxLength={500}
          value={evidence}
          onChange={(event) => setEvidence(event.target.value)}
          aria-describedby={`${evidenceId}-hint`}
        />
        <p id={`${evidenceId}-hint`} className="field__hint">
          Onde e como o provider confirmou: chamado, e-mail, extrato. Até 500 caracteres.
        </p>
      </div>
      {error && (
        <p className="field__error" role="alert">
          {error}
        </p>
      )}
      <div className="form__actions">
        <button type="submit" className="button button--primary" disabled={busy}>
          {busy ? "Registrando…" : "Registrar para aprovação"}
        </button>
        <button type="button" className="button" onClick={onCancel} disabled={busy}>
          Cancelar
        </button>
      </div>
    </form>
  );
}
