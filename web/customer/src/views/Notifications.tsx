import { api } from "@banking/web-shared/client";
import { formatDateTime } from "@banking/web-shared/money";
import { Empty, ErrorState, Loading } from "@banking/web-shared/States";
import { useResource } from "@banking/web-shared/useResource";
import type { NotificationView } from "../api/types";
import { accountLabel, useSelectedAccount } from "../bank";
import { Money } from "../components/Money";
import { notificationText } from "../lib/messages";

/** Avisos gerados pelos eventos da conta (consumidor de notificações, ADR-007). Chegam segundos depois da operação. */
export function Notifications() {
  const account = useSelectedAccount();
  const [notifications, reload] = useResource(() => api<NotificationView[]>(`/api/v1/accounts/${account.id}/notifications`), account.id);

  return (
    <>
      <header className="page-header">
        <h1>Avisos</h1>
        <p>{accountLabel(account)}. Cada movimentação gera um aviso, em geral em poucos segundos.</p>
      </header>

      {notifications.state === "loading" && <Loading what="os avisos" />}
      {notifications.state === "error" && <ErrorState message={notifications.message} onRetry={reload} />}
      {notifications.state === "ready" && notifications.data.length === 0 && (
        <Empty>
          <p>Nenhum aviso ainda. Depósitos, transferências e mudanças na conta aparecem aqui.</p>
          <button type="button" className="button" onClick={reload}>
            Atualizar
          </button>
        </Empty>
      )}
      {notifications.state === "ready" && notifications.data.length > 0 && (
        <>
          <ul className="notices">
            {notifications.data.map((notice) => (
              <li key={notice.id} className="notices__item">
                <div>
                  <p>{notificationText(notice.kind)}</p>
                  <p className="muted">{formatDateTime(notice.createdAt)}</p>
                </div>
                {notice.amount && notice.currency && <Money value={notice.amount} currency={notice.currency} />}
              </li>
            ))}
          </ul>
          <button type="button" className="button notices__refresh" onClick={reload}>
            Atualizar
          </button>
        </>
      )}
    </>
  );
}
