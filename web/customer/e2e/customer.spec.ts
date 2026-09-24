import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { accountOf, auditedOperations, balanceOf, cents, cpf, deposit } from "./seed";

const app = "http://localhost:5190";

/** Login pela página do próprio Keycloak, como um cliente faria. */
async function login(page: Page, username: string) {
  await page.goto("/");
  await page.getByLabel(/username|usuário/i).fill(username);
  await page.getByLabel(/password|senha/i).first().fill(`${username}-dev-only`);
  await page.getByRole("button", { name: /sign in|entrar/i }).click();
  await page.waitForURL(`${app}/**`);
}

async function expectNoAxeViolations(page: Page) {
  const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
  expect(results.violations.map((v) => `${v.id}: ${v.help}`)).toEqual([]);
}

/** Bruno manda, Alice recebe. Os saldos são comparados antes e depois: o backoffice mexe na conta da Alice. */
async function brunoAndAlice(request: Parameters<typeof accountOf>[0]) {
  const bruno = await accountOf(request, "bruno", "Bruno Dev");
  const alice = await accountOf(request, "alice", "Alice Dev");
  await deposit(request, bruno.id, "1000.00");
  return { bruno, alice };
}

async function startInternalTransfer(page: Page, to: string, amount: string) {
  await page.getByRole("link", { name: "Transferir" }).first().click();
  await page.getByLabel("Conta", { exact: true }).fill(to.replace(/^0+/, ""));
  await page.getByLabel("Valor (R$)").fill(amount);
  await page.getByRole("button", { name: "Revisar transferência" }).click();
  await expect(page.getByRole("heading", { name: "Confira a transferência" })).toBeFocused();
}

test("cliente novo se cadastra e abre a conta pelo app", async ({ page }) => {
  await login(page, "carla");

  await expect(page.getByRole("heading", { name: "Abra sua conta" })).toBeVisible();
  await expectNoAxeViolations(page);
  await page.getByLabel("Nome completo").fill("Carla Dev");
  await page.getByLabel("CPF").fill(cpf());
  await page.getByRole("button", { name: "Abrir conta" }).click();

  await expect(page.getByRole("heading", { name: "Sua conta" })).toBeVisible();
  await expect(page.locator(".balance__value")).toHaveText("R$ 0,00");
  await expect(page.getByText(/Nenhuma movimentação ainda/)).toBeVisible();
  await expectNoAxeViolations(page);
});

test("transferência interna passa pelo resumo e move o dinheiro uma vez", async ({ page, request }) => {
  const { bruno, alice } = await brunoAndAlice(request);
  const before = { bruno: await balanceOf(request, "bruno", bruno.id), alice: await balanceOf(request, "alice", alice.id) };

  await login(page, "bruno");
  await startInternalTransfer(page, alice.number, "123,45");
  await expect(page.getByText("R$ 123,45")).toBeVisible();
  // Titular mascarado: primeiro nome e inicial do sobrenome, que depende de quem cadastrou a Alice nesta execução.
  await expect(page.getByText(new RegExp(`^Alice [A-Z]\\., Agência 0001 · Conta ${alice.number}$`))).toBeVisible();
  await expectNoAxeViolations(page);
  await page.getByRole("button", { name: "Confirmar transferência" }).click();

  await expect(page.getByRole("heading", { name: "Transferência concluída" })).toBeFocused();
  expect(cents(await balanceOf(request, "bruno", bruno.id))).toBe(cents(before.bruno) - 12345n);
  expect(cents(await balanceOf(request, "alice", alice.id))).toBe(cents(before.alice) + 12345n);

  // O código do comprovante é o que a auditoria do banco encontra.
  const code = (await page.locator(".facts .code__value").innerText()).trim();
  expect(await auditedOperations(request, code)).toContain("transfer.create");

  await page.getByRole("link", { name: "Ver extrato" }).click();
  const line = page.locator(".statement__item").first();
  await expect(line.locator(".statement__line")).toContainText("R$ 123,45");
  await line.locator("summary").click();
  await expect(line.locator(".code__value")).toHaveText(code);
});

test("resposta perdida na rede: tentar de novo não transfere duas vezes", async ({ page, request }) => {
  const { bruno, alice } = await brunoAndAlice(request);
  await login(page, "bruno");
  await startInternalTransfer(page, alice.number, "77,77");
  const before = await balanceOf(request, "bruno", bruno.id);

  // A requisição chega à API e a transferência acontece, mas a resposta não volta ao navegador.
  const keys: string[] = [];
  let first = true;
  await page.route("**/api/v1/transfers", async (route) => {
    keys.push(route.request().headers()["idempotency-key"]!);
    if (first) {
      first = false;
      await route.fetch();
      await route.abort("failed");
      return;
    }

    await route.continue();
  });

  await page.getByRole("button", { name: "Confirmar transferência" }).click();
  await expect(page.getByText(/a transferência não será feita duas vezes/)).toBeVisible();
  await page.getByRole("button", { name: "Tentar de novo" }).click();

  await expect(page.getByRole("heading", { name: "Transferência concluída" })).toBeVisible();
  expect(keys).toHaveLength(2);
  expect(keys[0]).toBe(keys[1]);
  expect(cents(await balanceOf(request, "bruno", bruno.id))).toBe(cents(before) - 7777n);
});

test("clique duplo em confirmar faz uma transferência só", async ({ page, request }) => {
  const { bruno, alice } = await brunoAndAlice(request);
  await login(page, "bruno");
  await startInternalTransfer(page, alice.number, "11,11");
  const before = await balanceOf(request, "bruno", bruno.id);

  await page.getByRole("button", { name: "Confirmar transferência" }).dblclick();

  await expect(page.getByRole("heading", { name: "Transferência concluída" })).toBeVisible();
  expect(cents(await balanceOf(request, "bruno", bruno.id))).toBe(cents(before) - 1111n);
});

test("saldo insuficiente é recusado com mensagem clara", async ({ page, request }) => {
  const { alice } = await brunoAndAlice(request);
  await login(page, "bruno");
  await startInternalTransfer(page, alice.number, "19.999,99");

  await page.getByRole("button", { name: "Confirmar transferência" }).click();

  await expect(page.getByRole("alert")).toContainText("Saldo insuficiente para esse valor.");
  await expect(page.getByRole("button", { name: "Tentar de novo" })).toHaveCount(0);
});

test("transferência para outro banco é acompanhada até concluir", async ({ page, request }) => {
  await brunoAndAlice(request);
  await login(page, "bruno");

  await page.getByRole("link", { name: "Transferir" }).first().click();
  await page.getByLabel("Conta em outro banco").check();
  await page.getByLabel("Banco (ISPB)").fill("00000000");
  await page.getByLabel("Agência").fill("1234");
  await page.getByLabel("Conta", { exact: true }).fill(`SUCCESS-${Date.now()}`);
  await page.getByLabel("Valor (R$)").fill("50");
  await page.getByRole("button", { name: "Revisar transferência" }).click();
  await page.getByRole("button", { name: "Confirmar transferência" }).click();

  await expect(page.getByRole("heading", { name: "Transferência para outro banco" })).toBeVisible();
  await expect(page.locator("#tracking-status")).toHaveText("Concluída", { timeout: 30_000 });
  await expect(page.getByText("Esta tela se atualiza sozinha")).toHaveCount(0);
  await expectNoAxeViolations(page);
});

test("recusa do outro banco devolve o valor e o app diz isso", async ({ page, request }) => {
  const { bruno } = await brunoAndAlice(request);
  await login(page, "bruno");
  const before = await balanceOf(request, "bruno", bruno.id);

  await page.getByRole("link", { name: "Transferir" }).first().click();
  await page.getByLabel("Conta em outro banco").check();
  await page.getByLabel("Banco (ISPB)").fill("00000000");
  await page.getByLabel("Agência").fill("1234");
  await page.getByLabel("Conta", { exact: true }).fill(`FAIL-${Date.now()}`);
  await page.getByLabel("Valor (R$)").fill("40");
  await page.getByRole("button", { name: "Revisar transferência" }).click();
  await page.getByRole("button", { name: "Confirmar transferência" }).click();

  await expect(page.locator("#tracking-status")).toHaveText("Não concluída", { timeout: 30_000 });
  await expect(page.getByText("O valor voltou para a sua conta.")).toBeVisible();
  expect(await balanceOf(request, "bruno", bruno.id)).toBe(before);
});

test("avisos chegam pelos eventos da conta", async ({ page, request }) => {
  const { bruno } = await brunoAndAlice(request);
  await login(page, "bruno");
  await page.getByRole("link", { name: "Avisos" }).click();

  // O aviso passa por outbox, RabbitMQ e consumidor; chega em segundos, não na hora.
  await expect(async () => {
    await page.reload();
    await expect(page.getByText("Depósito recebido").first()).toBeVisible({ timeout: 1_000 });
  }).toPass({ timeout: 30_000 });
  await expectNoAxeViolations(page);
  expect(bruno.id).toBeTruthy();
});

test("operador não entra no app do cliente", async ({ page }) => {
  await login(page, "olga");

  await expect(page.getByRole("heading", { name: "Este acesso não é de cliente" })).toBeVisible();
});

test("o navegador não guarda token: só o cookie de sessão HttpOnly", async ({ page, context, request }) => {
  await brunoAndAlice(request);
  await login(page, "bruno");
  await expect(page.getByRole("heading", { name: "Sua conta" })).toBeVisible();

  const cookies = await context.cookies(app);
  const storage = await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }));

  // A sessão é um cookie só, que o ASP.NET divide em partes (__Host-bankingC1, C2...) quando passa do tamanho (ADR-011).
  expect(cookies.length).toBeGreaterThan(0);
  for (const cookie of cookies) {
    expect(cookie.name).toMatch(/^__Host-banking(C\d+)?$/);
    expect(cookie.httpOnly).toBe(true);
    expect(cookie.sameSite).toBe("Strict");
    expect(cookie.value).not.toContain("eyJ");
  }
  expect(await page.evaluate(() => document.cookie)).toBe("");
  expect(storage).not.toContain("eyJ");
});

test.describe("no celular", () => {
  test.use({ viewport: { width: 375, height: 740 } });

  test("conta, transferência e resumo cabem sem rolagem lateral", async ({ page, request }) => {
    const { alice } = await brunoAndAlice(request);
    await login(page, "bruno");
    const overflows = () => page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth);

    await expect(page.getByRole("heading", { name: "Sua conta" })).toBeVisible();
    expect(await overflows()).toBe(false);
    await startInternalTransfer(page, alice.number, "1,00");
    expect(await overflows()).toBe(false);
    await expectNoAxeViolations(page);
  });
});
