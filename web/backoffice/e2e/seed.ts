import type { APIRequestContext } from "@playwright/test";

// Monta dados direto na API (porta 5080), com tokens de desenvolvimento do Keycloak.
const keycloak = "http://localhost:8080/realms/banking/protocol/openid-connect/token";
const api = "http://localhost:5080";

export async function tokenFor(request: APIRequestContext, username: string): Promise<string> {
  const response = await request.post(keycloak, {
    form: { grant_type: "password", client_id: "banking-cli", username, password: `${username}-dev-only` },
  });
  if (!response.ok()) {
    throw new Error(`Token de ${username}: ${response.status()} ${await response.text()}`);
  }

  return (await response.json()).access_token as string;
}

async function call(request: APIRequestContext, token: string, method: "GET" | "POST", path: string, data?: unknown) {
  const response = await request.fetch(`${api}${path}`, {
    method,
    data,
    headers: { Authorization: `Bearer ${token}`, "Idempotency-Key": crypto.randomUUID().replaceAll("-", "") },
  });
  const body = await response.text();
  return { status: response.status(), body: body ? JSON.parse(body) : null };
}

/** CPF válido e aleatório: nenhum CPF real entra nos testes. */
function cpf(): string {
  const digits = Array.from({ length: 9 }, () => Math.floor(Math.random() * 10));
  for (const length of [9, 10]) {
    const sum = digits.slice(0, length).reduce((acc, digit, index) => acc + digit * (length + 1 - index), 0);
    const remainder = (sum * 10) % 11;
    digits.push(remainder === 10 ? 0 : remainder);
  }

  return digits.join("");
}

let accountId: string | null = null;

/** Conta da cliente alice, criada uma vez por execução. */
async function aliceAccount(request: APIRequestContext): Promise<string> {
  if (accountId) {
    return accountId;
  }

  const token = await tokenFor(request, "alice");
  await call(request, token, "POST", "/api/v1/customers", { name: "Alice E2E", document: cpf() });
  const accounts = await call(request, token, "GET", "/api/v1/accounts");
  const existing = (accounts.body as { id: string }[])[0];
  accountId = existing ? existing.id : (await call(request, token, "POST", "/api/v1/accounts", { currency: "BRL" })).body.id;
  return accountId!;
}

/** Depósito acima do limite de aprovação, pedido por `operator`: fica pendente. */
export async function pendingDeposit(request: APIRequestContext, operator: string, amount: string): Promise<{ id: string; accountId: string }> {
  const account = await aliceAccount(request);
  const token = await tokenFor(request, operator);
  const response = await call(request, token, "POST", `/api/v1/accounts/${account}/deposits`, { amount, currency: "BRL", reason: "aporte para teste E2E" });
  if (response.status !== 202) {
    throw new Error(`Depósito pendente não criado: ${response.status} ${JSON.stringify(response.body)}`);
  }

  return { id: response.body.id, accountId: account };
}

export async function depositStatus(request: APIRequestContext, depositId: string): Promise<string> {
  const token = await tokenFor(request, "olga");
  return (await call(request, token, "GET", `/api/v1/deposits/${depositId}`)).body.status;
}

/** Depósito dentro do limite: concluído na hora. */
export async function fundedAccount(request: APIRequestContext, amount: string): Promise<string> {
  const account = await aliceAccount(request);
  const token = await tokenFor(request, "olga");
  const response = await call(request, token, "POST", `/api/v1/accounts/${account}/deposits`, { amount, currency: "BRL", reason: "saldo para teste E2E" });
  if (response.status !== 201) {
    throw new Error(`Depósito não concluído: ${response.status} ${JSON.stringify(response.body)}`);
  }

  return account;
}

/** Transferência externa que o mock nunca responde: chega à revisão manual. */
export async function externalTransferInReview(request: APIRequestContext): Promise<string> {
  const account = await fundedAccount(request, "500.00");
  const token = await tokenFor(request, "alice");
  const created = await call(request, token, "POST", "/api/v1/external-transfers", {
    sourceAccountId: account,
    amount: "123.45",
    currency: "BRL",
    destination: { bank: "00000000", branch: "0001", account: `TIMEOUT-${Date.now()}` },
  });
  if (created.status !== 202) {
    throw new Error(`Transferência externa não criada: ${created.status} ${JSON.stringify(created.body)}`);
  }

  const id = created.body.id as string;
  for (let attempt = 0; attempt < 60; attempt++) {
    const view = await call(request, token, "GET", `/api/v1/external-transfers/${id}`);
    if (view.body.requiresManualReview) {
      return id;
    }

    await new Promise((resolve) => setTimeout(resolve, 500));
  }

  throw new Error("Transferência externa não chegou à revisão manual.");
}

export async function externalTransferStatus(request: APIRequestContext, id: string): Promise<string> {
  const token = await tokenFor(request, "alice");
  return (await call(request, token, "GET", `/api/v1/external-transfers/${id}`)).body.status;
}
