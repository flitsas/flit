import { expect, test, type Page } from "@playwright/test";

/**
 * HU #13852 (F08-H2) — listado de diagnósticos con filtros en el inicio.
 *
 * Artefacto E2E para QA: necesita la suite levantada (hub en 4022, gateway en 4002, core-diagnostico en 4029 con
 * `Diagnostico__Ambiente=LOCAL|DEV` y `Diagnostico__Simulacion__Habilitada=true`, y esta app en 4024). Excluido de `tsc` y
 * de Vitest; se corre con Playwright desde la raíz del repo, por ejemplo:
 *
 *   DIAGNOSTICO_URL=http://127.0.0.1:4024 E2E_USUARIO=... E2E_CLAVE=... \
 *   pnpm --dir frontend exec playwright test ../frontend-diagnostico/e2e/listado.spec.ts
 *
 * E2E_USUARIO es un usuario de la compañía con diagnosticos.crear y diagnosticos.ver. Las credenciales son de usuarios de
 * prueba del ambiente DEV y entran solo por variables de entorno.
 */
const APP = process.env.DIAGNOSTICO_URL ?? "http://127.0.0.1:4024";
/** Placa de prueba terminada en 1: la simulación de RUNT responde con éxito. */
const PLACA = process.env.E2E_PLACA_EXPRESS ?? "TST101";

async function entrar(page: Page, usuario: string, clave: string) {
  await page.goto(`${APP}/`);
  await page.waitForURL((url) => !url.href.startsWith(APP));
  await page.getByLabel(/correo|usuario/i).fill(usuario);
  await page.getByLabel(/contraseña/i).fill(clave);
  await page.getByRole("button", { name: /ingresar|iniciar sesión/i }).click();
  await page.waitForURL(`${APP}/`);
  await expect(page.getByRole("heading", { level: 1, name: "Diagnóstico vehicular" })).toBeVisible();
}

test.describe("listado de diagnósticos del inicio", () => {
  test("crea un Express, lo encuentra filtrando por nombre y abre su tablero", async ({ page }) => {
    test.skip(!process.env.E2E_USUARIO || !process.env.E2E_CLAVE, "Faltan E2E_USUARIO / E2E_CLAVE");
    await entrar(page, process.env.E2E_USUARIO!, process.env.E2E_CLAVE!);

    // Nombre único para encontrarlo sin depender de los diagnósticos que ya tenga la compañía.
    const nombre = `E2E listado ${Date.now()}`;
    await page.getByLabel(/placa o vin/i).fill(PLACA);
    await page.getByRole("button", { name: "Diagnosticar" }).click();
    const campoNombre = page.getByLabel(/nombre del diagnóstico/i);
    await campoNombre.fill(nombre);
    await page.getByRole("button", { name: "Lanzar diagnóstico" }).click();
    await page.waitForURL(/\/diagnosticos\/[0-9a-f-]{36}$/i);
    const tablero = page.url();

    // AC2: el filtro por nombre se refleja en la URL y la tabla solo muestra lo que cumple.
    await page.goto(`${APP}/`);
    await page.getByRole("searchbox", { name: "Nombre" }).fill(nombre);
    await page.waitForURL((url) => url.searchParams.get("nombre") === nombre);
    const tabla = page.getByRole("table", { name: "Diagnósticos de la compañía" });
    await expect(tabla.getByRole("row")).toHaveCount(2);
    const fila = tabla.getByRole("row", { name: new RegExp(nombre) });
    await expect(fila).toBeVisible();

    // AC1: la fila trae tipo, estado y creador; Enter en su enlace abre el tablero.
    await expect(fila.getByRole("cell").nth(3)).not.toBeEmpty();
    await fila.getByRole("link", { name: nombre }).press("Enter");
    await page.waitForURL(tablero);
    await expect(page.getByRole("table", { name: /semáforo de consultas/i })).toBeVisible();

    // Volver atrás conserva el filtro en la URL y en el campo.
    await page.goBack();
    await expect(page.getByRole("searchbox", { name: "Nombre" })).toHaveValue(nombre);
  });
});
