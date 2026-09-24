import { useState, type FormEvent } from "react";
import { useSearchParams } from "react-router";
import { api } from "@banking/web-shared/client";
import type { AuditLogView, AuditVerification } from "../api/types";
import { Empty, ErrorState, Loading } from "@banking/web-shared/States";
import { formatDateTime } from "@banking/web-shared/money";
import { useResource } from "@banking/web-shared/useResource";
import { useSession } from "../session";

export function Audit() {
  const session = useSession();
  const [params, setParams] = useSearchParams();
  const resourceId = params.get("resourceId") ?? "";
  const [draft, setDraft] = useState(resourceId);

  function search(event: FormEvent) {
    event.preventDefault();
    setParams(draft.trim() ? { resourceId: draft.trim() } : {});
  }

  return (
    <>
      <header className="page-header">
        <h1>Auditoria</h1>
        <p>
          Quem fez o quê, em ordem. Busque pelo id de uma operação (transferência, depósito, conta). O trace id leva ao mesmo
          pedido no Aspire Dashboard.
        </p>
      </header>

      <form className="toolbar" onSubmit={search} role="search">
        <div className="field">
          <label htmlFor="resource-id">Id do recurso</label>
          <input
            id="resource-id"
            type="text"
            value={draft}
            onChange={(event) => setDraft(event.target.value)}
            placeholder="ex.: id da transferência"
            spellCheck={false}
          />
        </div>
        <button type="submit" className="button button--primary">
          Buscar
        </button>
      </form>

      {resourceId ? <AuditTrail resourceId={resourceId} /> : <Empty>Informe um id para ver a trilha dele.</Empty>}

      {session.isAdmin && <ChainVerification />}
    </>
  );
}

function AuditTrail({ resourceId }: { resourceId: string }) {
  const [entries, reload] = useResource(
    () => api<AuditLogView[]>(`/api/v1/admin/audit?resourceId=${encodeURIComponent(resourceId)}`),
    resourceId,
  );

  if (entries.state === "loading") {
    return <Loading what="a trilha" />;
  }

  if (entries.state === "error") {
    return <ErrorState message={entries.message} onRetry={reload} />;
  }

  if (entries.data.length === 0) {
    return <Empty>Nenhum registro de auditoria para este id. Confira se é o id da operação, e não o da transação contábil.</Empty>;
  }

  return (
    <div className="table-wrap">
      <table>
        <caption className="visually-hidden">Trilha de auditoria de {resourceId}</caption>
        <thead>
          <tr>
            <th scope="col" className="num">Posição</th>
            <th scope="col">Quando</th>
            <th scope="col">Operação</th>
            <th scope="col">Resultado</th>
            <th scope="col">Quem</th>
            <th scope="col">Trace</th>
          </tr>
        </thead>
        <tbody>
          {entries.data.map((entry) => (
            <tr key={entry.position}>
              <td className="num">{entry.position}</td>
              <td>{formatDateTime(entry.occurredAt)}</td>
              <td className="mono">{entry.operation}</td>
              <td>{entry.outcome}</td>
              <td className="mono">{entry.actor}</td>
              <td className="mono">{entry.traceId ?? ""}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function ChainVerification() {
  const [result, setResult] = useState<AuditVerification | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function verify() {
    setBusy(true);
    setError(null);
    try {
      setResult(await api<AuditVerification>("/api/v1/admin/audit/verify"));
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="section" aria-labelledby="chain-title">
      <h2 id="chain-title" className="section__title">
        Integridade da trilha
      </h2>
      <p className="muted section__lead">
        Recalcula o hash encadeado de todos os registros fora do banco. Detecta alteração ou remoção feita por quem tem acesso
        direto ao Postgres.
      </p>
      <button type="button" className="button" onClick={verify} disabled={busy}>
        {busy ? "Verificando…" : "Verificar a cadeia"}
      </button>
      {error && <ErrorState message={error} onRetry={verify} />}
      {result && (
        <p className="verdict verdict--after-action" aria-live="polite">
          {result.isIntact ? (
            <span className="status status--success">Íntegra: {result.verifiedEntries} registros conferidos.</span>
          ) : (
            <span className="status status--danger">
              Adulterada na posição {result.brokenAtPosition}: {result.reason}
            </span>
          )}
        </p>
      )}
    </section>
  );
}
