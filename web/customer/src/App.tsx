import { api, ApiError } from "@banking/web-shared/client";
import { ErrorState, Loading } from "@banking/web-shared/States";
import { useResource } from "@banking/web-shared/useResource";
import { BrowserRouter, Navigate, NavLink, Route, Routes } from "react-router";
import type { AccountView, BffUser, CustomerView } from "./api/types";
import { BankContext, useBank } from "./bank";
import { ExternalTransferStatus } from "./views/ExternalTransferStatus";
import { Home } from "./views/Home";
import { Notifications } from "./views/Notifications";
import { Onboarding } from "./views/Onboarding";
import { Transfer } from "./views/Transfer";

async function logout() {
  const { logoutUrl } = await api<{ logoutUrl: string }>("/bff/logout", { method: "POST" });
  window.location.assign(logoutUrl);
}

type Loaded =
  | { kind: "no-access" }
  | { kind: "onboarding"; user: BffUser; customer: CustomerView | null }
  | { kind: "ready"; user: BffUser; customer: CustomerView; accounts: AccountView[] };

/** 403 do BFF quer dizer que quem entrou não é cliente; 404 em /customers/me, que ainda não fez o cadastro. */
async function loadBank(): Promise<Loaded> {
  let user: BffUser;
  try {
    user = await api<BffUser>("/bff/user");
  } catch (error) {
    if (error instanceof ApiError && error.status === 403) {
      return { kind: "no-access" };
    }

    throw error;
  }

  const customer = await api<CustomerView>("/api/v1/customers/me").catch((error: unknown) => {
    if (error instanceof ApiError && error.status === 404) {
      return null;
    }

    throw error;
  });
  if (!customer) {
    return { kind: "onboarding", user, customer: null };
  }

  const accounts = await api<AccountView[]>("/api/v1/accounts");
  return accounts.length === 0 ? { kind: "onboarding", user, customer } : { kind: "ready", user, customer, accounts };
}

export function App() {
  const [bank, reload] = useResource(loadBank);

  if (bank.state === "loading") {
    return (
      <main>
        <Loading what="sua conta" />
      </main>
    );
  }

  if (bank.state === "error") {
    return (
      <main>
        <ErrorState message={bank.message} onRetry={reload} />
      </main>
    );
  }

  const data = bank.data;
  if (data.kind === "no-access") {
    return (
      <main className="narrow">
        <header className="page-header">
          <h1>Este acesso não é de cliente</h1>
          <p>O app do Banking Core é para clientes. Operadores usam o backoffice. Entre com uma conta de cliente.</p>
        </header>
        <button type="button" className="button button--primary" onClick={() => void logout()}>
          Sair e entrar com outra conta
        </button>
      </main>
    );
  }

  if (data.kind === "onboarding") {
    return (
      <>
        <Topbar />
        <main className="narrow">
          <Onboarding customer={data.customer} onDone={reload} />
        </main>
      </>
    );
  }

  return (
    <BankContext.Provider value={data}>
      <BrowserRouter>
        <Topbar nav />
        <main>
          <Routes>
            <Route path="/" element={<Home />} />
            <Route path="/transferir" element={<Transfer />} />
            <Route path="/transferencias/externas/:id" element={<ExternalTransferStatus />} />
            <Route path="/avisos" element={<Notifications />} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </main>
      </BrowserRouter>
    </BankContext.Provider>
  );
}

function Topbar({ nav = false }: { nav?: boolean }) {
  return (
    <header className="topbar">
      <p className="topbar__name">Banking Core</p>
      {nav && <Nav />}
      <button type="button" className="button button--quiet" onClick={() => void logout()}>
        Sair
      </button>
    </header>
  );
}

function Nav() {
  const { customer } = useBank();
  return (
    <>
      <nav className="nav" aria-label="Principal">
        <NavLink to="/" end>
          Conta
        </NavLink>
        <NavLink to="/transferir">Transferir</NavLink>
        <NavLink to="/avisos">Avisos</NavLink>
      </nav>
      <p className="topbar__who">{customer.name}</p>
    </>
  );
}
