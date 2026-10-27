import { expect, test, type Page } from "@playwright/test";

/**
 * HU #13744 (F00.2-H3) — pantalla 10: configuración con pestañas Contrato y Operación.
 *
 * Artefacto E2E para QA: necesita la suite levantada (hub en 4022, gateway en 4002, core-diagnostico en 4029 y esta
 * app en 4024). Excluido de `tsc` y de Vitest; se corre con Playwright desde la raíz del repo, por ejemplo:
 *
 *   DIAGNOSTICO_URL=http://127.0.0.1:4024 E2E_ADMIN=... E2E_CLAVE_ADMIN=... \
 *   E2E_USUARIO=... E2E_CLAVE=... E2E_BACKOFFICE=... E2E_CLAVE_BACKOFFICE=... E2E_EMPRESA_CLIENTE_NOMBRE=<nombre> \
 *   pnpm --dir frontend exec playwright test ../frontend-diagnostico/e2e/configuracion.spec.ts
 *
 * Las credenciales son de usuarios de prueba del ambiente DEV y entran solo por variables de entorno.
 */
const APP = process.env.DIAGNOSTICO_URL ?? "http://127.0.0.1:4024";
const AJENOS = /usuario|\broles?\b|suscripci|retenci|purga/i;

async function entrar(page: Page, usuario: string, clave: string) {
  await page.goto(`${APP}/configuracion`);
  await page.waitForURL((url) => !url.href.startsWith(APP));
  await page.getByLabel(/correo|usuario/i).fill(usuario);
  await page.getByLabel(/contraseña/i).fill(clave);
  await page.getByRole("button", { name: /ingresar|iniciar sesión/i }).click();
  await page.waitForURL(`${APP}/configuracion`);
  await expect(page.getByRole("heading", { level: 1, name: "Configuración" })).toBeVisible();
}

async function sinCamposAjenos(page: Page) {
  const tabs = page.getByRole("tablist", { name: "Configuración de Diagnóstico" }).getByRole("tab");
  for (let i = 0; i < (await tabs.count()); i++) {
    await tabs.nth(i).click();
    const panel = page.getByRole("tabpanel");
    for (const control of await panel.locator("input, select, textarea, [role=switch]").all()) {
      const nombre = (await control.getAttribute("aria-label")) ?? (await control.evaluate((el) => el.closest("label")?.textContent ?? el.getAttribute("name") ?? ""));
      expect(nombre).not.toMatch(AJENOS);
    }
    await expect(panel.getByText(AJENOS)).toHaveCount(0);
  }
}

test.describe("pantalla 10", () => {
  test("admin: Contrato en solo lectura y Operación editable, sin campos ajenos", async ({ page }) => {
    test.skip(!process.env.E2E_ADMIN || !process.env.E2E_CLAVE_ADMIN, "Faltan E2E_ADMIN / E2E_CLAVE_ADMIN");
    await entrar(page, process.env.E2E_ADMIN!, process.env.E2E_CLAVE_ADMIN!);

    // Escenario 1: contrato visible y sin edición.
    const contrato = page.getByRole("tabpanel", { name: "Contrato" });
    await expect(contrato.getByText("Límite de vehículos por lote")).toBeVisible();
    await expect(contrato.getByRole("checkbox")).toHaveCount(0);
    await expect(page.getByRole("combobox", { name: "Empresa" })).toHaveCount(0);

    // Escenario 3: Operación editable con teclado.
    await page.getByRole("tab", { name: "Contrato" }).focus();
    await page.keyboard.press("ArrowRight");
    await expect(page.getByRole("tab", { name: "Operación" })).toHaveAttribute("aria-selected", "true");
    await expect(page.getByRole("button", { name: "Guardar operación" })).toBeEnabled();

    // Escenario 5: ningún perfil ve usuarios, roles, suscripción, retención ni purga.
    await sinCamposAjenos(page);
  });

  test("usuario sin editar: Operación deshabilitada", async ({ page }) => {
    test.skip(!process.env.E2E_USUARIO || !process.env.E2E_CLAVE, "Faltan E2E_USUARIO / E2E_CLAVE");
    await entrar(page, process.env.E2E_USUARIO!, process.env.E2E_CLAVE!);

    // Escenario 4.
    await page.getByRole("tab", { name: "Operación" }).click();
    await expect(page.getByRole("button", { name: "Guardar operación" })).toBeDisabled();
    await sinCamposAjenos(page);
  });

  test("backoffice FLIT: elige la empresa en el selector y edita su contrato", async ({ page }) => {
    test.skip(
      !process.env.E2E_BACKOFFICE || !process.env.E2E_CLAVE_BACKOFFICE || !process.env.E2E_EMPRESA_CLIENTE_NOMBRE,
      "Faltan E2E_BACKOFFICE / E2E_CLAVE_BACKOFFICE / E2E_EMPRESA_CLIENTE_NOMBRE",
    );
    await entrar(page, process.env.E2E_BACKOFFICE!, process.env.E2E_CLAVE_BACKOFFICE!);

    // Escenarios 2 y 7: la empresa se busca por nombre en el selector (GET /backoffice/empresas), sin escribir su uuid.
    const nombre = process.env.E2E_EMPRESA_CLIENTE_NOMBRE!;
    const selector = page.getByRole("combobox", { name: "Empresa" });
    await selector.fill(nombre);
    await page.getByRole("option").filter({ hasText: nombre }).first().click();
    await expect(page.getByRole("heading", { name: nombre })).toBeVisible();
    await expect(page.getByRole("spinbutton", { name: "Límite de vehículos por lote" })).toBeVisible();
    await expect(page.getByRole("switch", { name: "Suspender la redacción con IA" })).toBeVisible();
    await expect(page.getByRole("checkbox", { name: "RUNT vehículo" })).toBeVisible();
    await sinCamposAjenos(page);
  });
});
