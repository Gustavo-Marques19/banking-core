import { render } from "@testing-library/react";
import type { ReactNode } from "react";
import { MemoryRouter, Route, Routes } from "react-router";
import { vi } from "vitest";
import type { AccountView } from "../api/types";
import { BankContext } from "../bank";

export const myAccount: AccountView = { id: "acc-me", customerId: "c-me", branch: "0001", number: "00000001", currency: "BRL", status: "active" };

/** Rotas de teste: "METHOD path" → resposta, ou função que decide a cada chamada. */
export type ApiRoutes = Record<string, unknown | ((init: RequestInit) => Response | Promise<Response>)>;

export function stubApi(routes: ApiRoutes) {
  const fetchMock = vi.fn(async (path: string, init: RequestInit = {}) => {
    const route = routes[`${init.method ?? "GET"} ${path}`];
    if (route === undefined) {
      return new Response(JSON.stringify({ title: "sem rota no teste" }), { status: 404 });
    }

    return typeof route === "function" ? route(init) : new Response(JSON.stringify(route), { status: 200 });
  });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

export function json(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status });
}

export function renderAt(path: string, element: ReactNode, pattern = path) {
  return render(
    <BankContext.Provider
      value={{ user: { name: "alice", subject: "sub-me", roles: ["customer"] }, customer: { id: "c-me", name: "Alice Dev", document: "***", status: "active" }, accounts: [myAccount] }}
    >
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path={pattern} element={element} />
          <Route path="/transferencias/externas/:id" element={<p>tela de acompanhamento</p>} />
        </Routes>
      </MemoryRouter>
    </BankContext.Provider>,
  );
}
