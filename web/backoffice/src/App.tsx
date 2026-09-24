import { BrowserRouter, Navigate, NavLink, Route, Routes } from "react-router";
import { api, ApiError } from "./api/client";
import type { BffUser } from "./api/types";
import { ErrorState, Loading } from "./components/States";
import { useResource } from "./lib/useResource";
import { SessionContext, toSession, useSession } from "./session";
import { Approvals } from "./views/Approvals";
import { Audit } from "./views/Audit";
import { ManualReview } from "./views/ManualReview";
import { Reconciliation } from "./views/Reconciliation";

async function logout() {
  const { logoutUrl } = await api<{ logoutUrl: string }>("/bff/logout", { method: "POST" });
  window.location.assign(logoutUrl);
}

/** Quem entrou com um papel que não é de backoffice recebe 403 do BFF: vira null, não erro. */
async function loadUser(): Promise<BffUser | null> {
  try {
    return await api<BffUser>("/bff/user");
  } catch (error) {
    if (error instanceof ApiError && error.status === 403) {
      return null;
    }

    throw error;
  }
}

export function App() {
  const [user, reload] = useResource(loadUser);

  if (user.state === "loading") {
    return (
      <main>
        <Loading what="sua sessão" />
      </main>
    );
  }

  if (user.state === "error") {
    return (
      <main>
        <ErrorState message={user.message} onRetry={reload} />
      </main>
    );
  }

  if (user.data === null) {
    return (
      <main>
        <header className="page-header">
          <h1>Sem acesso ao backoffice</h1>
          <p>Sua conta não tem o papel de operador nem de administrador. Entre com outra conta.</p>
        </header>
        <button type="button" className="button button--primary" onClick={() => void logout()}>
          Sair e entrar com outra conta
        </button>
      </main>
    );
  }

  return (
    <SessionContext.Provider value={toSession(user.data)}>
      <BrowserRouter>
        <Shell />
      </BrowserRouter>
    </SessionContext.Provider>
  );
}

/** Mostra só as telas que o papel do usuário pode usar: nenhum link leva a um 403. */
function Shell() {
  const session = useSession();

  return (
    <>
      <header className="topbar">
        <p className="topbar__name">
          Banking Core <span>· Backoffice</span>
        </p>
        <nav className="nav" aria-label="Principal">
          {session.isOperator && <NavLink to="/" end>Aprovações</NavLink>}
          {session.isOperator && <NavLink to="/revisao-manual">Revisão manual</NavLink>}
          <NavLink to="/reconciliacao">Reconciliação</NavLink>
          <NavLink to="/auditoria">Auditoria</NavLink>
        </nav>
        <div className="session">
          <span>
            {session.name ?? "operador"} <span className="muted">· {session.roles.join(", ")}</span>
          </span>
          <button type="button" className="button" onClick={() => void logout()}>
            Sair
          </button>
        </div>
      </header>
      <main>
        <Routes>
          <Route path="/" element={session.isOperator ? <Approvals /> : <Navigate to="/auditoria" replace />} />
          {session.isOperator && <Route path="/revisao-manual" element={<ManualReview />} />}
          <Route path="/reconciliacao" element={<Reconciliation />} />
          <Route path="/auditoria" element={<Audit />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </main>
    </>
  );
}
