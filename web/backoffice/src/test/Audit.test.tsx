import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SessionContext, toSession } from "../session";
import { Audit, operationIdOf } from "../views/Audit";

const ledgerId = "01a0d52d-0000-7000-8000-00000000000a";
const transferId = "01a0d52d-0000-7000-8000-00000000000b";

function stubApi(routes: Record<string, { status?: number; body: unknown }>) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (path: string) => {
      const route = routes[path];
      return new Response(JSON.stringify(route?.body ?? { title: "inexistente" }), { status: route ? (route.status ?? 200) : 404 });
    }),
  );
}

function renderAudit(id: string) {
  render(
    <SessionContext.Provider value={toSession({ name: "olga", subject: "s", roles: ["operator"] })}>
      <MemoryRouter initialEntries={[`/auditoria?resourceId=${id}`]}>
        <Routes>
          <Route path="/auditoria" element={<Audit />} />
        </Routes>
      </MemoryRouter>
    </SessionContext.Provider>,
  );
}

const entry = { position: 7, occurredAt: "2026-09-24T12:00:00Z", actor: "sub-alice", operation: "transfer.create", resourceType: "transfer", resourceId: transferId, outcome: "completed", traceId: null };

afterEach(() => vi.unstubAllGlobals());

describe("Auditoria", () => {
  it("id de lançamento do extrato leva à trilha da operação", async () => {
    stubApi({
      [`/api/v1/admin/audit?resourceId=${ledgerId}`]: { body: [] },
      [`/api/v1/ledger/transactions/${ledgerId}`]: { body: { id: ledgerId, externalId: `transfer:${transferId}` } },
      [`/api/v1/admin/audit?resourceId=${transferId}`]: { body: [entry] },
    });

    renderAudit(ledgerId);

    expect(await screen.findByText("transfer.create")).toBeTruthy();
    expect(screen.getByText(/Esse id é de um lançamento contábil/)).toBeTruthy();
  });

  it("código sem registro diz o que conferir", async () => {
    stubApi({ [`/api/v1/admin/audit?resourceId=${transferId}`]: { body: [] } });

    renderAudit(transferId);

    expect(await screen.findByText(/Nenhum registro de auditoria para este código/)).toBeTruthy();
  });

  it.each([
    ["transfer:" + transferId, transferId],
    [`external-transfer:${transferId}:reservation`, transferId],
    ["seed", null],
  ])("operationIdOf(%s)", (externalId, expected) => {
    expect(operationIdOf(externalId)).toBe(expected);
  });
});
