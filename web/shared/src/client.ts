/** Erro no formato ProblemDetails da API, com o código estável em `code`. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string | null,
    message: string,
  ) {
    super(message);
  }
}

type Navigate = (url: string) => void;

let navigate: Navigate = (url) => window.location.assign(url);

/** Troca a navegação nos testes. */
export function setNavigator(next: Navigate) {
  navigate = next;
}

/**
 * Toda chamada passa pelo BFF, com o cookie de sessão. Quem muda estado manda X-CSRF (ADR-011).
 * Sem sessão, volta ao login preservando a tela atual.
 */
export async function api<T>(path: string, init: { method?: string; body?: unknown; idempotencyKey?: string } = {}): Promise<T> {
  const method = init.method ?? "GET";
  const headers: Record<string, string> = { Accept: "application/json" };
  if (method !== "GET") {
    headers["X-CSRF"] = "1";
  }
  if (init.idempotencyKey) {
    headers["Idempotency-Key"] = init.idempotencyKey;
  }
  if (init.body !== undefined) {
    headers["Content-Type"] = "application/json";
  }

  const response = await fetch(path, {
    method,
    headers,
    credentials: "same-origin",
    body: init.body === undefined ? undefined : JSON.stringify(init.body),
  });

  if (response.status === 401) {
    navigate(`/bff/login?returnUrl=${encodeURIComponent(window.location.pathname + window.location.search)}`);
    throw new ApiError(401, "unauthenticated", "Sessão expirada. Redirecionando para o login.");
  }

  const text = await response.text();
  const payload = text ? (JSON.parse(text) as Record<string, unknown>) : null;
  if (!response.ok) {
    const title = typeof payload?.title === "string" ? payload.title : `Erro ${response.status}`;
    const code = typeof payload?.code === "string" ? payload.code : null;
    throw new ApiError(response.status, code, title);
  }

  return payload as T;
}
