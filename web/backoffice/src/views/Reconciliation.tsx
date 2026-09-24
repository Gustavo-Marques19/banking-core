import { useState, type FormEvent } from "react";
import { api } from "../api/client";
import type { ReconciliationReport } from "../api/types";
import { ErrorState } from "../components/States";
import { formatDateTime } from "../lib/money";

/** O que cada verificação quer dizer, para quem lê a divergência às 3h da manhã. */
const meaning: Record<string, string> = {
  trial_balance: "Débitos e créditos do ledger não somam zero. Algum lançamento ficou desbalanceado.",
  balance_projection: "O saldo materializado de uma conta não bate com a soma dos lançamentos dela.",
  negative_balance: "Uma conta de cliente está com saldo negativo.",
  clearing_mismatch: "O saldo de Clearing não é igual ao das transferências externas em aberto.",
  settlement_missing_in_ledger: "O provider liquidou algo que o ledger não tem.",
  settlement_pending_in_ledger: "O provider liquidou uma transferência que ainda está aberta aqui; a próxima consulta deve resolver.",
  settlement_missing_at_provider: "O ledger liquidou algo que não está no extrato do provider.",
  settlement_amount_mismatch: "Ledger e provider liquidaram a mesma transferência com valores diferentes.",
  settlement_unavailable: "O extrato do provider não pôde ser consultado.",
};

function today(): string {
  return new Intl.DateTimeFormat("en-CA", { timeZone: "America/Sao_Paulo" }).format(new Date());
}

export function Reconciliation() {
  const [date, setDate] = useState(today());
  const [report, setReport] = useState<ReconciliationReport | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function run(event?: FormEvent) {
    event?.preventDefault();
    setBusy(true);
    setError(null);
    try {
      setReport(await api<ReconciliationReport>(`/api/v1/admin/reconciliation?date=${encodeURIComponent(date)}`));
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure));
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <header className="page-header">
        <h1>Reconciliação</h1>
        <p>
          Confere o ledger contra ele mesmo (balancete, saldos, Clearing) e contra o extrato do provider no dia escolhido. Roda
          sozinha a cada 5 minutos; aqui você roda na hora.
        </p>
      </header>

      <form className="toolbar" onSubmit={run}>
        <div className="field">
          <label htmlFor="statement-date">Dia do extrato</label>
          <input id="statement-date" type="date" value={date} onChange={(event) => setDate(event.target.value)} required />
        </div>
        <button type="submit" className="button button--primary" disabled={busy}>
          {busy ? "Conferindo…" : "Rodar reconciliação"}
        </button>
      </form>

      {error && <ErrorState message={error} onRetry={() => void run()} />}

      {report && (
        <section aria-live="polite">
          <p className="verdict">
            {report.isConsistent ? (
              <span className="status status--success">Tudo confere.</span>
            ) : (
              <span className="status status--danger">
                {report.findings.length} {report.findings.length === 1 ? "divergência" : "divergências"}
              </span>
            )}{" "}
            <span className="muted">Rodada em {formatDateTime(report.ranAt)}.</span>
          </p>
          {report.findings.length > 0 && (
            <ul className="findings">
              {report.findings.map((finding, index) => (
                <li key={index} className="finding">
                  <strong>{meaning[finding.check] ?? finding.check}</strong>
                  <span className="mono muted">{finding.check}</span>
                  <span>{finding.detail}</span>
                </li>
              ))}
            </ul>
          )}
        </section>
      )}
    </>
  );
}
