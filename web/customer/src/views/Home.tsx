import { api } from "@banking/web-shared/client";
import { formatDateTime } from "@banking/web-shared/money";
import { Empty, ErrorState, Loading } from "@banking/web-shared/States";
import { useResource } from "@banking/web-shared/useResource";
import { useState } from "react";
import { Link, useSearchParams } from "react-router";
import type { BalanceView, StatementLine, StatementView } from "../api/types";
import { accountLabel, useBank, useSelectedAccount } from "../bank";
import { Money } from "../components/Money";
import { OperationCode } from "../components/OperationCode";
import { messageFor } from "../lib/messages";

const pageSize = 20;

export function Home() {
  const { accounts } = useBank();
  const account = useSelectedAccount();
  const [, setParams] = useSearchParams();
  const [balance, reloadBalance] = useResource(
    () => api<BalanceView>(`/api/v1/accounts/${account.id}/balance`),
    account.id,
  );

  return (
    <>
      <header className="page-header">
        <h1>Sua conta</h1>
        <p>{accountLabel(account)}</p>
      </header>

      {accounts.length > 1 && (
        <div className="field field--inline">
          <label htmlFor="account">Conta</label>
          <select id="account" value={account.id} onChange={(e) => setParams({ conta: e.target.value })}>
            {accounts.map((a) => (
              <option key={a.id} value={a.id}>
                {accountLabel(a)}
              </option>
            ))}
          </select>
        </div>
      )}

      <section className="balance" aria-labelledby="balance-title">
        <h2 id="balance-title" className="balance__label">
          Saldo disponível
        </h2>
        {balance.state === "loading" && <p className="balance__value balance__value--pending">Carregando…</p>}
        {balance.state === "error" && <ErrorState message={balance.message} onRetry={reloadBalance} />}
        {balance.state === "ready" && (
          <>
            <p className="balance__value">
              <Money value={balance.data.availableBalance} currency={balance.data.currency} />
            </p>
            <p className="balance__as-of">Atualizado em {formatDateTime(balance.data.asOf)}</p>
          </>
        )}
        {account.status !== "active" && (
          <p className="status status--warning">Conta bloqueada para movimentação. Fale com o banco.</p>
        )}
        <Link className="button button--primary" to={accounts.length > 1 ? `/transferir?conta=${account.id}` : "/transferir"}>
          Transferir
        </Link>
      </section>

      <Statement key={account.id} accountId={account.id} />
    </>
  );
}

/** Extrato do mais novo para o mais antigo, com páginas acrescentadas por cursor. */
function Statement({ accountId }: { accountId: string }) {
  const [first, reload] = useResource(() => api<StatementView>(`/api/v1/accounts/${accountId}/transactions?limit=${pageSize}`), accountId);
  const [more, setMore] = useState<{ lines: StatementLine[]; cursor: number | null } | null>(null);
  const [loadingMore, setLoadingMore] = useState(false);
  const [moreError, setMoreError] = useState<string | null>(null);

  if (first.state === "loading") {
    return <Loading what="o extrato" />;
  }

  if (first.state === "error") {
    return <ErrorState message={first.message} onRetry={reload} />;
  }

  const lines = [...first.data.lines, ...(more?.lines ?? [])];
  const cursor = more ? more.cursor : first.data.nextCursor;

  async function loadMore() {
    setLoadingMore(true);
    setMoreError(null);
    try {
      const page = await api<StatementView>(`/api/v1/accounts/${accountId}/transactions?limit=${pageSize}&before=${cursor}`);
      setMore((previous) => ({ lines: [...(previous?.lines ?? []), ...page.lines], cursor: page.nextCursor }));
    } catch (error) {
      setMoreError(messageFor(error));
    } finally {
      setLoadingMore(false);
    }
  }

  return (
    <section className="section" aria-labelledby="statement-title">
      <h2 id="statement-title" className="section__title">
        Extrato
      </h2>
      {lines.length === 0 ? (
        <Empty>Nenhuma movimentação ainda. Depósitos e transferências aparecem aqui assim que acontecem.</Empty>
      ) : (
        <>
          <p className="statement__hint">Toque numa movimentação para ver o código dela, que o banco usa para localizar a operação.</p>
          <ul className="statement">
            {lines.map((line) => (
              <li key={line.sequence}>
                {/* Detalhe nativo: abre com teclado e leitor de tela sem JavaScript próprio. */}
                <details className="statement__item">
                  <summary className="statement__line">
                    <div className="statement__what">
                      <p>{line.description}</p>
                      <p className="muted">{formatDateTime(line.postedAt)}</p>
                    </div>
                    <div className="statement__amounts">
                      <p>
                        <Money value={line.amount} currency={first.data.currency} direction={line.direction} />
                      </p>
                      <p className="muted">
                        saldo <Money value={line.balanceAfter} currency={first.data.currency} />
                      </p>
                    </div>
                  </summary>
                  <div className="statement__detail">
                    {line.operationId ? (
                      <p>
                        Código da operação: <OperationCode code={line.operationId} />
                      </p>
                    ) : (
                      <p className="muted">Lançamento sem código de operação.</p>
                    )}
                  </div>
                </details>
              </li>
            ))}
          </ul>
        </>
      )}
      {moreError && (
        <p className="field__error" role="alert">
          {moreError}
        </p>
      )}
      {cursor !== null && (
        <button type="button" className="button statement__more" onClick={() => void loadMore()} disabled={loadingMore}>
          {loadingMore ? "Carregando…" : "Carregar mais"}
        </button>
      )}
    </section>
  );
}
