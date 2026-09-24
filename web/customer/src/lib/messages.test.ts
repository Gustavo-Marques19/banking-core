import { ApiError } from "@banking/web-shared/client";
import { describe, expect, it } from "vitest";
import { messageFor, notificationText } from "./messages";

describe("messageFor", () => {
  it("usa o código estável, não o título técnico", () => {
    expect(messageFor(new ApiError(422, "insufficient_funds", "Transferência recusada."))).toBe("Saldo insuficiente para esse valor.");
  });

  it("erro de servidor não mostra detalhe interno", () => {
    expect(messageFor(new ApiError(500, null, "NpgsqlException: timeout"))).toBe("Algo falhou do nosso lado. Tente de novo em instantes.");
  });

  it("falha de rede vira aviso de conexão", () => {
    expect(messageFor(new TypeError("Failed to fetch"))).toMatch(/Sem conexão/);
  });
});

describe("notificationText", () => {
  it("recusa traz o motivo em linguagem simples", () => {
    expect(notificationText("transfer_rejected:insufficient_funds")).toBe("Transferência recusada: saldo insuficiente para esse valor");
  });

  it("tipo desconhecido não quebra a lista", () => {
    expect(notificationText("algo_novo")).toBe("Movimentação na conta");
  });
});
