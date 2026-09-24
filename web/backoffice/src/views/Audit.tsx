import { useState, type FormEvent } from "react";
import { useSearchParams } from "react-router";
import { api, ApiError } from "@banking/web-shared/client";
import type { AuditLogView, AuditVerification, LedgerTransactionView } from "../api/types";
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
          Quem fez o quê, em ordem. Busque pelo código da operação (o que o cliente vê no comprovante) ou pelo id de um
          lançamento contábil. O trace id leva ao mesmo pedido no Aspire Dashboard.
        </p>
      </header>

      <form className="toolbar" onSubmit={search} role="search">
        <div className="field">
          <label htmlFor="resource-id">Código da operação ou id do lançamento</label>
          <input
            id="resource-id"
            type="text"
            value={draft}
            onChange={(event) => setDraft(event.target.value)}
            placeholder="ex.: código do comprovante"
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

interface Trail {
  entries: AuditLogView[];
  /** Preenchido quando o id buscado era de um lançamento contábil e a trilha é da operação dele. */
  viaLedger: { externalId: string; operationId: string } | null;
}

const trailOf = (id: string) => api<AuditLogView[]>(`/api/v1/admin/audit?resourceId=${encodeURIComponent(id)}`);

/** "transfer:{id}", "external-transfer:{id}:reservation"...: o id da operação é o segundo pedaço (LedgerTransaction.OperationIdOf). */
export function operationIdOf(externalId: string): string | null {
  const id = externalId.split(":")[1];
  return id && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id) ? id : null;
}

/**
 * Nada na auditoria com esse id? Talvez seja um lançamento do extrato: a auditoria registra a operação, não o
 * lançamento. Admin não lê lançamentos (403), e aí fica a busca direta.
 */
async function loadTrail(resourceId: string): Promise<Trail> {
  const entries = await trailOf(resourceId);
  if (entries.length > 0 || !/^[0-9a-f-]{36}$/i.test(resourceId)) {
    return { entries, viaLedger: null };
  }

  const ledger = await api<LedgerTransactionView>(`/api/v1/ledger/transactions/${resourceId}`).catch((error: unknown) => {
    if (error instanceof ApiError && (error.status === 404 || error.status === 403)) {
      return null;
    }

    throw error;
  });
  const operationId = ledger ? operationIdOf(ledger.externalId) : null;
  return ledger && operationId
    ? { entries: await trailOf(operationId), viaLedger: { externalId: ledger.externalId, operationId } }
    : { entries, viaLedger: null };
}

function AuditTrail({ resourceId }: { resourceId: string }) {
  const [trail, reload] = useResource(() => loadTrail(resourceId), resourceId);

  if (trail.state === "loading") {
    return <Loading what="a trilha" />;
  }

  if (trail.state === "error") {
    return <ErrorState message={trail.message} onRetry={reload} />;
  }

  const { entries, viaLedger } = trail.data;
  if (entries.length === 0) {
    return <Empty>Nenhum registro de auditoria para este código. Confira se ele foi copiado inteiro.</Empty>;
  }

  return (
    <>
      {viaLedger && (
        <p className="section__lead">
          Esse id é de um lançamento contábil (<span className="mono">{viaLedger.externalId}</span>). Abaixo, a trilha da
          operação que o gerou, <span className="mono">{viaLedger.operationId}</span>.
        </p>
      )}
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
            {entries.map((entry) => (
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
    </>
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
