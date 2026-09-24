import type { APIRequestContext } from "@playwright/test";

// Monta dados direto na API (porta 5080), com tokens de desenvolvimento do Keycloak.
const keycloak = "http://localhost:8080/realms/banking/protocol/openid-connect/token";
const api = "http://localhost:5080";

async function tokenFor(request: APIRequestContext, username: string): Promise<string> {
  const response = await request.post(keycloak, {
    form: { grant_type: "password", client_id: "banking-cli", username, password: `${username}-dev-only` },
  });
  if (!response.ok()) {
    throw new Error(`Token de ${username}: ${response.status()} ${await response.text()}`);
  }

  return (await response.json()).access_token as string;
}

async function call(request: APIRequestContext, username: string, method: "GET" | "POST", path: string, data?: unknown) {
  const response = await request.fetch(`${api}${path}`, {
    method,
    data,
    headers: { Authorization: `Bearer ${await tokenFor(request, username)}`, "Idempotency-Key": crypto.randomUUID() },
  });
  const body = await response.text();
  return { status: response.status(), body: body ? JSON.parse(body) : null };
}

/** CPF válido e aleatório: nenhum CPF real entra nos testes. */
export function cpf(): string {
  const digits = Array.from({ length: 9 }, () => Math.floor(Math.random() * 10));
  for (const length of [9, 10]) {
    const sum = digits.slice(0, length).reduce((acc, digit, index) => acc + digit * (length + 1 - index), 0);
    const remainder = (sum * 10) % 11;
    digits.push(remainder === 10 ? 0 : remainder);
  }

  return digits.join("");
}

export interface SeededAccount {
  id: string;
  number: string;
}

/** Cadastro e conta do cliente, criados uma vez. O backoffice pode já ter cadastrado a alice na mesma execução. */
export async function accountOf(request: APIRequestContext, username: string, name: string): Promise<SeededAccount> {
  await call(request, username, "POST", "/api/v1/customers", { name, document: cpf() });
  const accounts = (await call(request, username, "GET", "/api/v1/accounts")).body as SeededAccount[];
  return accounts[0] ?? (await call(request, username, "POST", "/api/v1/accounts", { currency: "BRL" })).body;
}

/** Depósito dentro do limite de aprovação, feito pela operadora olga. */
export async function deposit(request: APIRequestContext, accountId: string, amount: string) {
  const response = await call(request, "olga", "POST", `/api/v1/accounts/${accountId}/deposits`, { amount, currency: "BRL", reason: "saldo para teste E2E" });
  if (response.status !== 201) {
    throw new Error(`Depósito não concluído: ${response.status} ${JSON.stringify(response.body)}`);
  }
}

export async function balanceOf(request: APIRequestContext, username: string, accountId: string): Promise<string> {
  return (await call(request, username, "GET", `/api/v1/accounts/${accountId}/balance`)).body.availableBalance as string;
}

/** Operações registradas na auditoria para um código, vistas pela operadora olga. */
export async function auditedOperations(request: APIRequestContext, code: string): Promise<string[]> {
  const response = await call(request, "olga", "GET", `/api/v1/admin/audit?resourceId=${code}`);
  return (response.body as { operation: string }[]).map((entry) => entry.operation);
}

/** Soma em centavos, para comparar saldos sem passar dinheiro por número de ponto flutuante. */
export function cents(amount: string): bigint {
  const [integer = "0", fraction = ""] = amount.split(".");
  return BigInt(integer) * 100n + BigInt(fraction.padEnd(2, "0"));
}
