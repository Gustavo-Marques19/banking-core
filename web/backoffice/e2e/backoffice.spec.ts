import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { depositStatus, externalTransferInReview, externalTransferStatus, pendingDeposit } from "./seed";

/** Login pela página do próprio Keycloak, como um operador faria. */
async function login(page: Page, username: string) {
  await page.goto("/");
  await page.getByLabel(/username|usuário/i).fill(username);
  await page.getByLabel(/password|senha/i).first().fill(`${username}-dev-only`);
  await page.getByRole("button", { name: /sign in|entrar/i }).click();
  await page.waitForURL("http://localhost:5180/**");
}

async function expectNoAxeViolations(page: Page) {
  const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
  expect(results.violations.map((v) => `${v.id}: ${v.help}`)).toEqual([]);
}

test("outro operador aprova o depósito pedido pela olga", async ({ page, request }) => {
  const deposit = await pendingDeposit(request, "olga", "15000.00");

  await login(page, "otto");
  const item = page.getByRole("listitem").filter({ hasText: "15.000,00" }).first();
  await expect(item).toContainText("Pedido por outro operador");
  await item.getByRole("button", { name: "Aprovar" }).click();

  const dialog = page.getByRole("dialog");
  await expect(dialog).toContainText("R$ 15.000,00");
  await dialog.getByRole("button", { name: "Confirmar aprovação" }).click();

  await expect(dialog).toBeHidden();
  await expect.poll(() => depositStatus(request, deposit.id)).toBe("completed");
});

test("quem pediu não consegue aprovar", async ({ page, request }) => {
  await pendingDeposit(request, "olga", "12345.67");

  await login(page, "olga");
  const item = page.getByRole("listitem").filter({ hasText: "12.345,67" }).first();

  await expect(item).toContainText("Você fez este pedido. Outro operador precisa decidir.");
  await expect(item.getByRole("button", { name: "Aprovar" })).toHaveCount(0);
});

test("dá para decidir só com o teclado, e Esc fecha o diálogo", async ({ page, request }) => {
  await pendingDeposit(request, "olga", "11111.11");

  await login(page, "otto");
  const approve = page.getByRole("listitem").filter({ hasText: "11.111,11" }).first().getByRole("button", { name: "Aprovar" });
  await approve.focus();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("dialog")).toBeVisible();

  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).toBeHidden();
  await expect(approve).toBeFocused();
});

test("telas passam na verificação de acessibilidade (axe, WCAG AA)", async ({ page }) => {
  await login(page, "otto");
  for (const path of ["/", "/revisao-manual", "/reconciliacao", "/auditoria"]) {
    await page.goto(path);
    await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
    await expectNoAxeViolations(page);
  }
});

test("reconciliação roda e confere", async ({ page }) => {
  await login(page, "otto");
  await page.getByRole("link", { name: "Reconciliação" }).click();
  await page.getByRole("button", { name: "Rodar reconciliação" }).click();

  await expect(page.getByText("Tudo confere.")).toBeVisible();
});

test("cliente não entra no backoffice", async ({ page }) => {
  await login(page, "alice");

  await expect(page.getByRole("heading", { name: "Sem acesso ao backoffice" })).toBeVisible();
});

test("a página não recebe token e o cookie de sessão é HttpOnly", async ({ page, context }) => {
  await login(page, "otto");

  const cookies = await context.cookies();
  const session = cookies.find((c) => c.name === "__Host-backoffice");
  expect(session?.httpOnly).toBe(true);
  expect(session?.sameSite).toBe("Strict");
  expect(await page.evaluate(() => document.cookie)).not.toContain("backoffice");
  const storage = await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }));
  expect(storage).not.toContain("eyJ");
});

test("recusar um depósito não cria dinheiro", async ({ page, request }) => {
  const deposit = await pendingDeposit(request, "olga", "22222.22");

  await login(page, "otto");
  await page.getByRole("listitem").filter({ hasText: "22.222,22" }).first().getByRole("button", { name: "Recusar" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Confirmar recusa" }).click();

  await expect.poll(() => depositStatus(request, deposit.id)).toBe("rejected");
});

test("revisão manual: um operador registra o desfecho e outro aprova", async ({ page, request }) => {
  const transferId = await externalTransferInReview(request);

  await login(page, "otto");
  await page.getByRole("link", { name: "Revisão manual" }).click();
  const item = page.getByRole("listitem").filter({ hasText: "123,45" }).first();
  await item.getByRole("button", { name: "Registrar desfecho" }).click();
  await item.getByLabel(/Falhou/).check();
  await item.getByLabel("Evidência").fill("Provider confirmou por chamado que a ordem não foi recebida.");
  await item.getByRole("button", { name: "Registrar para aprovação" }).click();
  await expect(page.getByRole("status").filter({ hasText: "Desfecho registrado" })).toBeVisible();

  await page.context().clearCookies();
  await login(page, "olga");
  const resolution = page.getByRole("listitem").filter({ hasText: "Resolução manual" }).filter({ hasText: "123,45" }).first();
  await resolution.getByRole("button", { name: "Aprovar" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Confirmar aprovação" }).click();

  await expect.poll(() => externalTransferStatus(request, transferId)).toBe("failed");
});

test("auditoria mostra a trilha de uma operação", async ({ page, request }) => {
  const deposit = await pendingDeposit(request, "olga", "33333.33");

  await login(page, "otto");
  await page.getByRole("link", { name: "Auditoria" }).click();
  await page.getByLabel("Id do recurso").fill(deposit.id);
  await page.getByRole("button", { name: "Buscar" }).click();

  await expect(page.getByRole("cell", { name: "deposit.create" })).toBeVisible();
});

test("admin verifica a integridade da trilha", async ({ page }) => {
  await login(page, "ada");
  await expect(page.getByRole("heading", { name: "Auditoria" })).toBeVisible();
  await page.getByRole("button", { name: "Verificar a cadeia" }).click();

  await expect(page.getByText(/Íntegra: \d+ registros conferidos/)).toBeVisible();
});

test("sair encerra a sessão e volta ao login", async ({ page }) => {
  await login(page, "otto");
  await page.getByRole("button", { name: "Sair" }).click();

  await page.waitForURL("http://localhost:8080/**");
  await expect(page.getByLabel(/username|usuário/i)).toBeVisible();
});

test("no celular (375 px) nenhuma tela transborda na horizontal", async ({ page, request }) => {
  await pendingDeposit(request, "olga", "44444.44");
  await page.setViewportSize({ width: 375, height: 812 });

  await login(page, "otto");
  for (const path of ["/", "/revisao-manual", "/reconciliacao", "/auditoria"]) {
    await page.goto(path);
    await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
    expect(overflow, `transbordamento em ${path}`).toBeLessThanOrEqual(0);
  }
});
