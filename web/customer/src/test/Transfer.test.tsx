import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Transfer } from "../views/Transfer";
import { json, renderAt, stubApi } from "./render";

const bruno = { id: "acc-bruno", branch: "0001", number: "00000002", currency: "BRL", holderName: "Bruno D." };
const lookup = "GET /api/v1/accounts/lookup?branch=0001&number=2";
const created = (init: RequestInit) =>
  json(201, { id: "t1", sourceAccountId: "acc-me", destinationAccountId: "acc-bruno", amount: JSON.parse(String(init.body)).amount, currency: "BRL", status: "completed" });

function keysOf(fetchMock: ReturnType<typeof stubApi>) {
  return fetchMock.mock.calls
    .filter(([path, init]) => path === "/api/v1/transfers" && init?.method === "POST")
    .map(([, init]) => (init!.headers as Record<string, string>)["Idempotency-Key"]);
}

async function fillAndReview(amount = "1.234,50") {
  const user = userEvent.setup();
  renderAt("/transferir", <Transfer />);
  await user.type(screen.getByLabelText("Conta"), "2");
  await user.type(screen.getByLabelText("Valor (R$)"), amount);
  await user.click(screen.getByRole("button", { name: "Revisar transferência" }));
  return user;
}

afterEach(() => vi.unstubAllGlobals());

describe("Transferir", () => {
  it("valor inválido não chega à API", async () => {
    const fetchMock = stubApi({});

    await fillAndReview("12,345");

    expect(await screen.findByText(/Informe um valor maior que zero/)).toBeTruthy();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("resumo mostra o titular mascarado e o valor antes de enviar", async () => {
    const fetchMock = stubApi({ [lookup]: bruno });

    await fillAndReview();

    expect(await screen.findByRole("heading", { name: "Confira a transferência" })).toBeTruthy();
    expect(screen.getByText("R$ 1.234,50")).toBeTruthy();
    expect(screen.getByText(/Bruno D\., Agência 0001 · Conta 00000002/)).toBeTruthy();
    expect(keysOf(fetchMock)).toEqual([]);
  });

  it("clique duplo em confirmar manda uma requisição só, com o valor em formato da API", async () => {
    const fetchMock = stubApi({ [lookup]: bruno, "POST /api/v1/transfers": created });
    const user = await fillAndReview();

    await user.dblClick(await screen.findByRole("button", { name: "Confirmar transferência" }));

    expect(await screen.findByRole("heading", { name: "Transferência concluída" })).toBeTruthy();
    expect(screen.getByText("t1", { selector: ".code__value" })).toBeTruthy();
    expect(keysOf(fetchMock)).toHaveLength(1);
    const body = JSON.parse(String(fetchMock.mock.calls.find(([path]) => path === "/api/v1/transfers")![1]!.body));
    expect(body).toMatchObject({ sourceAccountId: "acc-me", destinationAccountId: "acc-bruno", amount: "1234.50", currency: "BRL" });
  });

  it("depois de falha de rede, tentar de novo repete a mesma chave (ADR-005)", async () => {
    let calls = 0;
    const fetchMock = stubApi({
      [lookup]: bruno,
      "POST /api/v1/transfers": (init: RequestInit) => {
        calls++;
        if (calls === 1) {
          throw new TypeError("Failed to fetch");
        }

        return created(init);
      },
    });
    const user = await fillAndReview();

    await user.click(await screen.findByRole("button", { name: "Confirmar transferência" }));
    expect(await screen.findByText(/a transferência não será feita duas vezes/)).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Tentar de novo" }));

    expect(await screen.findByRole("heading", { name: "Transferência concluída" })).toBeTruthy();
    const keys = keysOf(fetchMock);
    expect(keys).toHaveLength(2);
    expect(keys[0]).toBe(keys[1]);
  });

  it("corrigir os dados cria outra intenção, com outra chave", async () => {
    let calls = 0;
    const fetchMock = stubApi({
      [lookup]: bruno,
      "POST /api/v1/transfers": () => (++calls === 1 ? json(503, { title: "ocupado", code: "contention" }) : json(201, { id: "t2", status: "completed" })),
    });
    const user = await fillAndReview();

    await user.click(await screen.findByRole("button", { name: "Confirmar transferência" }));
    await screen.findByText(/O banco está ocupado agora/);
    await user.click(screen.getByRole("button", { name: "Corrigir dados" }));
    await user.click(screen.getByRole("button", { name: "Revisar transferência" }));
    await user.click(await screen.findByRole("button", { name: "Confirmar transferência" }));

    await screen.findByRole("heading", { name: "Transferência concluída" });
    const keys = keysOf(fetchMock);
    expect(keys).toHaveLength(2);
    expect(keys[0]).not.toBe(keys[1]);
  });

  it("recusa mostra o motivo em linguagem simples e não oferece repetir", async () => {
    stubApi({ [lookup]: bruno, "POST /api/v1/transfers": () => json(422, { title: "Transferência recusada.", code: "insufficient_funds" }) });
    const user = await fillAndReview();

    await user.click(await screen.findByRole("button", { name: "Confirmar transferência" }));

    const alert = await screen.findByRole("alert");
    expect(alert.textContent).toContain("Saldo insuficiente para esse valor.");
    expect(screen.queryByRole("button", { name: /Tentar de novo|Confirmar/ })).toBeNull();
  });

  it("conta de destino inexistente para no formulário", async () => {
    stubApi({});

    await fillAndReview();

    expect(await screen.findByText("Nenhuma conta com essa agência e número.")).toBeTruthy();
    expect(screen.getByLabelText("Conta").getAttribute("aria-invalid")).toBe("true");
  });

  it("não deixa transferir para a própria conta", async () => {
    stubApi({ "GET /api/v1/accounts/lookup?branch=0001&number=1": { ...bruno, id: "acc-me", number: "00000001" } });
    const user = userEvent.setup();
    renderAt("/transferir", <Transfer />);

    await user.type(screen.getByLabelText("Conta"), "1");
    await user.type(screen.getByLabelText("Valor (R$)"), "10");
    await user.click(screen.getByRole("button", { name: "Revisar transferência" }));

    expect(await screen.findByText(/Essa é a conta de origem/)).toBeTruthy();
  });

  it("transferência para outro banco vai para o acompanhamento", async () => {
    const fetchMock = stubApi({ "POST /api/v1/external-transfers": () => json(202, { id: "x1", status: "created" }) });
    const user = userEvent.setup();
    renderAt("/transferir", <Transfer />);

    await user.click(screen.getByLabelText("Conta em outro banco"));
    await user.type(screen.getByLabelText("Banco (ISPB)"), "00000000");
    await user.type(screen.getByLabelText("Agência"), "1234");
    await user.type(screen.getByLabelText("Conta"), "5678-9");
    await user.type(screen.getByLabelText("Valor (R$)"), "50");
    await user.click(screen.getByRole("button", { name: "Revisar transferência" }));
    await user.click(await screen.findByRole("button", { name: "Confirmar transferência" }));

    expect(await screen.findByText("tela de acompanhamento")).toBeTruthy();
    const init = fetchMock.mock.calls.find(([path]) => path === "/api/v1/external-transfers")![1]!;
    expect((init.headers as Record<string, string>)["Idempotency-Key"]).toMatch(/^[0-9a-f-]{36}$/);
    await waitFor(() => expect(JSON.parse(String(init.body)).destination).toEqual({ bank: "00000000", branch: "1234", account: "5678-9" }));
  });
});
