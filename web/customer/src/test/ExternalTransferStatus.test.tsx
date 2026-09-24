import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ExternalTransferStatus as Status } from "../api/types";
import { ExternalTransferStatus } from "../views/ExternalTransferStatus";
import { json, renderAt, stubApi } from "./render";

const transfer = (status: Status, requiresManualReview = false) => ({
  id: "x1",
  sourceAccountId: "acc-me",
  amount: "50.00",
  currency: "BRL",
  destination: { bank: "00000000", branch: "1234", account: "5678-9" },
  status,
  rejectionReason: null,
  failureReason: null,
  requiresManualReview,
  createdAt: "2026-09-24T12:00:00Z",
  updatedAt: "2026-09-24T12:00:01Z",
});

const path = "/transferencias/externas/x1";
const get = "GET /api/v1/external-transfers/x1";

afterEach(() => vi.unstubAllGlobals());

describe("Acompanhamento da transferência externa", () => {
  it("consulta de novo até o estado final e então para", async () => {
    const answers: Status[] = ["unknown", "completed"];
    const fetchMock = stubApi({ [get]: () => json(200, transfer(answers.shift() ?? "completed")) });

    renderAt(path, <ExternalTransferStatus />, "/transferencias/externas/:id");

    expect(await screen.findByText("Enviando ao outro banco")).toBeTruthy();
    expect(await screen.findByText("Concluída", { selector: "#tracking-status" }, { timeout: 4000 })).toBeTruthy();
    const callsAtEnd = fetchMock.mock.calls.length;
    await new Promise((resolve) => setTimeout(resolve, 2500));
    expect(fetchMock.mock.calls.length).toBe(callsAtEnd);
  });

  it("só oferece cancelar antes do envio, e cancelar devolve o valor", async () => {
    stubApi({ [get]: transfer("created"), "POST /api/v1/external-transfers/x1/cancel": transfer("cancelled") });
    const user = userEvent.setup();
    renderAt(path, <ExternalTransferStatus />, "/transferencias/externas/:id");

    await user.click(await screen.findByRole("button", { name: "Cancelar transferência" }));
    await user.click(screen.getAllByRole("button", { name: "Cancelar transferência" }).at(-1)!);

    expect(await screen.findByText(/O valor voltou para a sua conta/)).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Cancelar transferência" })).toBeNull();
  });

  it("revisão manual avisa que o valor está reservado e não sai duas vezes", async () => {
    stubApi({ [get]: transfer("unknown", true) });

    renderAt(path, <ExternalTransferStatus />, "/transferencias/externas/:id");

    expect(await screen.findByText("Em análise pela nossa equipe")).toBeTruthy();
    expect(screen.getByText(/não será cobrado duas vezes/)).toBeTruthy();
  });
});
