import { afterEach, describe, expect, it, vi } from "vitest";
import { api, ApiError, setNavigator } from "./client";

function mockFetch(status: number, body: unknown) {
  const fetchMock = vi.fn().mockResolvedValue(new Response(body === null ? "" : JSON.stringify(body), { status }));
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

afterEach(() => vi.unstubAllGlobals());

describe("api", () => {
  it("GET não manda o header CSRF", async () => {
    const fetchMock = mockFetch(200, { ok: true });

    await api("/api/v1/x");

    expect(fetchMock.mock.calls[0]![1].headers["X-CSRF"]).toBeUndefined();
    expect(fetchMock.mock.calls[0]![1].credentials).toBe("same-origin");
  });

  it("POST manda X-CSRF (ADR-011)", async () => {
    const fetchMock = mockFetch(200, { ok: true });

    await api("/api/v1/x", { method: "POST", body: { a: 1 } });

    expect(fetchMock.mock.calls[0]![1].headers["X-CSRF"]).toBe("1");
  });

  it("manda a chave de idempotência quando a operação tem uma", async () => {
    const fetchMock = mockFetch(201, { id: "t1" });

    await api("/api/v1/transfers", { method: "POST", body: {}, idempotencyKey: "chave-1" });

    expect(fetchMock.mock.calls[0]![1].headers["Idempotency-Key"]).toBe("chave-1");
  });

  it("401 leva ao login preservando a tela", async () => {
    mockFetch(401, null);
    const navigate = vi.fn();
    setNavigator(navigate);

    await expect(api("/api/v1/x")).rejects.toBeInstanceOf(ApiError);

    expect(navigate).toHaveBeenCalledWith(expect.stringMatching(/^\/bff\/login\?returnUrl=/));
  });

  it("erro da API chega com o código estável", async () => {
    mockFetch(403, { title: "Quem pediu não pode aprovar.", code: "self_approval_not_allowed" });

    const error = await api("/api/v1/x", { method: "POST" }).catch((e: unknown) => e);

    expect(error).toMatchObject({ status: 403, code: "self_approval_not_allowed", message: "Quem pediu não pode aprovar." });
  });
});
