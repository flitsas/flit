import { expect, test, type Page } from "@playwright/test";
import { lanzarExpress } from "./express";

/**
 * HU #13852 (F08-H2) — listado de diagnósticos con filtros en el inicio.
 * Ola 2026-11-15 (FE-4): el inicio del listado es /diagnosticos; «/» es el Dashboard.
 *
 * Artefacto E2E para QA: necesita la suite levantada (hub en 4022, gateway en 4002, core-diagnostico en 4032 con
 * `Diagnostico__Ambiente=LOCAL|DEV` y `Diagnostico__Simulacion__Habilitada=true`, y esta app en 4024). Excluido de `tsc` y
 * de Vitest; se corre con Playwright desde la raíz del repo, por ejemplo:
 *
 *   DIAGNOSTICO_URL=http://127.0.0.1:4024 E2E_USUARIO=... E2E_CLAVE=... \
 *   pnpm --dir frontend exec playwright test ../frontend-diagnostico/e2e/listado.spec.ts
 *
 * E2E_USUARIO es un usuario de la compañía con diagnosticos.crear y diagnosticos.ver. Las credenciales son de usuarios de
 * prueba del ambiente DEV y entran solo por variables de entorno.
 *
 * Lista congelada (HU #14562 · AC55): nombres accesibles y atributos data-* de los que depende este spec.
 * Si uno cambia por decisión de UX, el spec se ajusta en el mismo PR de la pantalla.
 *   - button «/ingresar|iniciar sesión/i»
 *   - heading «Diagnóstico vehicular»
 *   - tablist «Vista del inicio» · tab «Diagnósticos» · tabpanel «Diagnósticos» (HU #14577 AC20: la pestaña, su panel y el
 *     h2 del listado se llaman igual; se distinguen por rol)
 *   - searchbox «Nombre»
 *   - table «Diagnósticos de la compañía»
 *   - row (sin nombre)
 *   - row «new RegExp(nombre)»
 *   - cell (sin nombre)
 *   - link (sin nombre)
 *   - table «/semáforo de consultas/i»
 *   - etiqueta «/correo|usuario/i»
 *   - etiqueta «Contraseña» (exacta: el hub añadió el botón «Mostrar contraseña»)
 *   Del helper express.ts (lanzarExpress):
 *     - textbox «Placa o VIN»
 *     - button «Diagnosticar en línea»
 *     - textbox «Nombre del diagnóstico»
 *     - button «/^(Continuar|Iniciar diagnóstico)$/»
 *     - button «Lanzar diagnóstico»
 *     - link «Ver tablero»
 */
const APP = process.env.DIAGNOSTICO_URL ?? "http://127.0.0.1:4024";
/** Placa de prueba terminada en 1: la simulación de RUNT responde con éxito. */
const PLACA = process.env.E2E_PLACA_EXPRESS ?? "TST101";

async function entrar(page: Page, usuario: string, clave: string) {
  // Ola 2026-11-15 (FE-4): «/» es el Dashboard; el listado y la tarjeta «Placa o VIN» viven en /diagnosticos.
  await page.goto(`${APP}/diagnosticos`);
  await page.waitForURL((url) => !url.href.startsWith(APP));
  await page.getByLabel(/correo|usuario/i).fill(usuario);
  await page.getByLabel("Contraseña", { exact: true }).fill(clave);
  await page.getByRole("button", { name: /ingresar|iniciar sesión/i }).click();
  await page.waitForURL(`${APP}/diagnosticos`);
  await expect(page.getByRole("heading", { level: 1, name: "Diagnóstico vehicular" })).toBeVisible();
}

test.describe("listado de diagnósticos del inicio", () => {
  test("crea un Express, lo encuentra filtrando por nombre y abre su tablero", async ({ page }) => {
    test.skip(!process.env.E2E_USUARIO || !process.env.E2E_CLAVE, "Faltan E2E_USUARIO / E2E_CLAVE");
    await entrar(page, process.env.E2E_USUARIO!, process.env.E2E_CLAVE!);

    // Nombre único para encontrarlo sin depender de los diagnósticos que ya tenga la compañía.
    const nombre = `E2E listado ${Date.now()}`;
    // HU #14085: el inicio abre la pantalla única en Express con la placa precargada.
    await lanzarExpress(page, PLACA, nombre);
    const tablero = page.url();

    // AC2: el filtro por nombre se refleja en la URL y la tabla solo muestra lo que cumple.
    // HU #14577 AC20: el listado vive en la pestaña «Diagnósticos» (activa sin ?vista=); se busca dentro de su panel.
    await page.goto(`${APP}/diagnosticos`);
    const pestana = page.getByRole("tablist", { name: "Vista del inicio" }).getByRole("tab", { name: "Diagnósticos" });
    await expect(pestana).toHaveAttribute("aria-selected", "true");
    const panel = page.getByRole("tabpanel", { name: "Diagnósticos" });
    await panel.getByRole("searchbox", { name: "Nombre" }).fill(nombre);
    await page.waitForURL((url) => url.searchParams.get("nombre") === nombre);
    const tabla = panel.getByRole("table", { name: "Diagnósticos de la compañía" });
    await expect(tabla.getByRole("row")).toHaveCount(2);
    const fila = tabla.getByRole("row", { name: new RegExp(nombre) });
    await expect(fila).toBeVisible();

    // AC1: la fila trae tipo, estado y creador; Enter en su enlace abre el tablero.
    await expect(fila.getByRole("cell").nth(3)).not.toBeEmpty();
    await fila.getByRole("link", { name: nombre }).press("Enter");
    // HU #14562: el enlace lleva «?desde=<filtros>» para que la miga vuelva al listado filtrado; se compara la ruta.
    await page.waitForURL((url) => url.pathname === new URL(tablero).pathname);
    await expect(page.getByRole("table", { name: /semáforo de consultas/i })).toBeVisible();

    // Volver atrás conserva el filtro en la URL y en el campo, en la pestaña «Diagnósticos».
    await page.goBack();
    await expect(pestana).toHaveAttribute("aria-selected", "true");
    await expect(panel.getByRole("searchbox", { name: "Nombre" })).toHaveValue(nombre);
  });
});
