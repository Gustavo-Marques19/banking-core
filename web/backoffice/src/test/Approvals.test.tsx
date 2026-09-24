import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SessionContext, toSession } from "../session";
import { Approvals } from "../views/Approvals";

const me = "operador-eu";

function stubApi(routes: Record<string, unknown>) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (path: string) => {
      const body = routes[path];
      return body === undefined ? new Response("", { status: 404 }) : new Response(JSON.stringify(body), { status: 200 });
    }),
  );
}

function renderApprovals() {
  render(
    <SessionContext.Provider value={toSession({ name: "olga", subject: me, roles: ["operator"] })}>
      <MemoryRouter>
        <Approvals />
      </MemoryRouter>
    </SessionContext.Provider>,
  );
}

const deposit = (requestedBy: string) => ({
  id: "d1",
  accountId: "a1",
  amount: "15000.00",
  currency: "BRL",
  reason: "aporte inicial",
  status: "pending_approval",
  rejectionReason: null,
  requestedBy,
  decidedBy: null,
  createdAt: "2026-09-24T12:00:00Z",
});

afterEach(() => vi.unstubAllGlobals());

describe("Aprovações", () => {
  it("fila vazia explica o que aparece ali", async () => {
    stubApi({
      "/api/v1/operations/pending-deposits": [],
      "/api/v1/operations/pending-reversals": [],
      "/api/v1/operations/pending-manual-resolutions": [],
    });

    renderApprovals();

    expect(await screen.findByText(/Nenhum pedido esperando decisão/)).toBeTruthy();
  });

  it("quem pediu não vê os botões de decisão", async () => {
    stubApi({
      "/api/v1/operations/pending-deposits": [deposit(me)],
      "/api/v1/operations/pending-reversals": [],
      "/api/v1/operations/pending-manual-resolutions": [],
      "/api/v1/accounts/a1": { id: "a1", branch: "0001", number: "00000042", status: "active" },
    });

    renderApprovals();

    expect(await screen.findByText("Você fez este pedido. Outro operador precisa decidir.")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Aprovar" })).toBeNull();
  });

  it("pedido de outro operador mostra valor formatado e as ações", async () => {
    stubApi({
      "/api/v1/operations/pending-deposits": [deposit("outro")],
      "/api/v1/operations/pending-reversals": [],
      "/api/v1/operations/pending-manual-resolutions": [],
      "/api/v1/accounts/a1": { id: "a1", branch: "0001", number: "00000042", status: "active" },
    });

    renderApprovals();

    expect(await screen.findByRole("button", { name: "Aprovar" })).toBeTruthy();
    expect(screen.getByText("15.000,00")).toBeTruthy();
    expect(screen.getByText("0001/00000042")).toBeTruthy();
  });

  it("falha ao carregar mostra o erro e deixa tentar de novo", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify({ title: "API fora" }), { status: 503 })));

    renderApprovals();

    expect(await screen.findByRole("alert")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Tentar de novo" })).toBeTruthy();
  });
});
