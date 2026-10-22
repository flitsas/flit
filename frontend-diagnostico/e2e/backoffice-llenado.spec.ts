import { expect, test, type Page } from "@playwright/test";

/**
 * HU #13802 (F06-H5) — backoffice FLIT: elegir compañía, abrir un lote con error, llenar SIMIT vehículo a mano y verlo en
 * verde con la marca «Manual»; un admin de compañía no ve la entrada y la ruta le muestra la 403.
 *
 * Artefacto E2E para QA: necesita la suite levantada (hub en 4022, gateway en 4002, core-diagnostico en 4029 con
 * `Diagnostico__Ambiente=LOCAL|DEV` y `Diagnostico__Simulacion__Habilitada=true`, y esta app en 4024) y un lote de la
 * compañía de prueba con SIMIT vehículo en ERROR (la simulación lo da con el escenario de falla de SIMIT). Excluido de `tsc`
 * y de Vitest; se corre con Playwright desde la raíz del repo, por ejemplo:
 *
 *   DIAGNOSTICO_URL=http://127.0.0.1:4024 E2E_BACKOFFICE_USUARIO=... E2E_BACKOFFICE_CLAVE=... E2E_EMPRESA=<uuid> \
 *   E2E_ADMIN_USUARIO=... E2E_ADMIN_CLAVE=... \
 *   pnpm --dir frontend exec playwright test ../frontend-diagnostico/e2e/backoffice-llenado.spec.ts
 *
 * E2E_BACKOFFICE_USUARIO es un usuario de la compañía FLIT con el rol backoffice_diagnostico (diagnostico.backoffice.ver y
 * diagnostico.consultas.llenar); E2E_ADMIN_USUARIO, el admin de una compañía cliente. Las credenciales son de usuarios de
 * prueba del ambiente DEV y entran solo por variables de entorno.
 */
const APP = process.env.DIAGNOSTICO_URL ?? "http://127.0.0.1:4024";

async function entrar(page: Page, usuario: string, clave: string) {
  await page.goto(`${APP}/`);
  await page.waitForURL((url) => !url.href.startsWith(APP));
  await page.getByLabel(/correo|usuario/i).fill(usuario);
  await page.getByLabel(/contraseña/i).fill(clave);
  await page.getByRole("button", { name: /ingresar|iniciar sesión/i }).click();
  await page.waitForURL(`${APP}/`);
}

test.describe("backoffice: llenado manual", () => {
  test("AC1+AC2: elige la compañía, filtra con errores, llena SIMIT vehículo y la celda queda verde con «Manual»", async ({ page }) => {
    const { E2E_BACKOFFICE_USUARIO: usuario, E2E_BACKOFFICE_CLAVE: clave, E2E_EMPRESA: empresa } = process.env;
    test.skip(!usuario || !clave || !empresa, "Faltan E2E_BACKOFFICE_USUARIO / E2E_BACKOFFICE_CLAVE / E2E_EMPRESA");
    await entrar(page, usuario!, clave!);

    // La entrada del dock existe solo para este perfil.
    await page.getByRole("link", { name: "Backoffice" }).first().click();
    await page.waitForURL(/\/backoffice\/diagnosticos/);

    await page.getByLabel("Identificador de la compañía").fill(empresa!);
    await page.getByRole("button", { name: "Ver lotes" }).click();
    await page.getByLabel("Solo lotes con errores").check();
    await expect(page).toHaveURL(/con_errores=true/);

    const lotes = page.getByRole("table", { name: /lotes de la compañía/i });
    await expect(lotes).toBeVisible();
    for (const fila of await lotes.getByRole("row").filter({ has: page.getByRole("link") }).all()) await expect(fila).toContainText(/con error/);

    // Abre el primer lote con error y busca un SIMIT vehículo en ERROR.
    await lotes.getByRole("link").first().click();
    await page.waitForURL(/\/backoffice\/diagnosticos\/[0-9a-f-]{36}\?empresa=/i);
    const grilla = page.getByRole("table", { name: /semáforo de consultas/i });
    await expect(grilla).toBeVisible();
    const semaforo = grilla.getByRole("button", { name: /^Ver detalle de SIMIT vehículo de .+: Error$/ }).first();
    test.skip((await semaforo.count()) === 0, "El lote elegido no tiene SIMIT vehículo en error");
    const celda = grilla.locator("[data-celda]").filter({ has: semaforo });

    await semaforo.click();
    await page.getByRole("dialog").getByRole("button", { name: "Llenar a mano" }).click();
    const form = page.getByRole("dialog", { name: /llenar a mano/i });
    await form.getByLabel("Semáforo").selectOption("VERDE");
    await form.getByRole("button", { name: "Confirmar llenado" }).click();

    await expect(celda).toHaveAttribute("data-estado", "COMPLETADO");
    await expect(celda.getByText("Completada")).toBeVisible();
    await expect(celda.getByText("Manual")).toBeVisible();
  });

  test("AC4: el admin de la compañía no ve el menú de backoffice y la ruta le muestra la 403", async ({ page }) => {
    const { E2E_ADMIN_USUARIO: usuario, E2E_ADMIN_CLAVE: clave } = process.env;
    test.skip(!usuario || !clave, "Faltan E2E_ADMIN_USUARIO / E2E_ADMIN_CLAVE");
    await entrar(page, usuario!, clave!);

    await expect(page.getByRole("link", { name: "Backoffice" })).toHaveCount(0);
    await page.goto(`${APP}/backoffice/diagnosticos`);
    await page.waitForURL(/\/403/);
    await expect(page.getByRole("heading", { level: 1, name: "No tienes acceso" })).toBeVisible();
  });
});
