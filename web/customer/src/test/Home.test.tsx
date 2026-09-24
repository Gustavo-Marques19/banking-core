import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Home } from "../views/Home";
import { renderAt, stubApi } from "./render";

const line = (operationId: string | null) => ({
  transactionId: "ledger-1",
  operationId,
  type: "internal_transfer",
  description: "Transferência 00000001 para 00000002",
  direction: "debit",
  amount: "10.00",
  balanceAfter: "90.00",
  sequence: 2,
  postedAt: "2026-09-24T12:00:00Z",
});

function stubAccount(lines: unknown[]) {
  stubApi({
    "GET /api/v1/accounts/acc-me/balance": { accountId: "acc-me", ledgerBalance: "90.00", availableBalance: "90.00", currency: "BRL", asOf: "2026-09-24T12:00:00Z" },
    "GET /api/v1/accounts/acc-me/transactions?limit=20": { accountId: "acc-me", currency: "BRL", lines, nextCursor: null },
  });
}

afterEach(() => vi.unstubAllGlobals());

describe("Extrato", () => {
  it("abrir uma linha mostra o código da operação, para copiar", async () => {
    stubAccount([line("0192a3b4-0000-7000-8000-000000000001")]);
    // userEvent instala uma área de transferência de teste em navigator.clipboard.
    const user = userEvent.setup();
    renderAt("/", <Home />);

    await user.click(await screen.findByText("Transferência 00000001 para 00000002"));
    await user.click(screen.getByRole("button", { name: "Copiar" }));

    expect(screen.getByText("0192a3b4-0000-7000-8000-000000000001")).toBeTruthy();
    expect(await navigator.clipboard.readText()).toBe("0192a3b4-0000-7000-8000-000000000001");
    expect(await screen.findByRole("button", { name: "Copiado" })).toBeTruthy();
  });

  it("lançamento sem operação diz isso em vez de mostrar código vazio", async () => {
    stubAccount([line(null)]);

    renderAt("/", <Home />);

    expect(await screen.findByText("Lançamento sem código de operação.")).toBeTruthy();
  });
});
