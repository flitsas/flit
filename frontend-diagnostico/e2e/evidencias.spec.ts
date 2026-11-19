import { expect, test, type Page } from "@playwright/test";
import { lanzarExpress } from "./express";

/**
 * HU #13851 (F07-H6) — descargas del tablero: PDF del vehículo y Excel del lote.
 *
 * Artefacto E2E para QA: necesita la suite levantada (hub en 4022, gateway en 4002, core-diagnostico en 4032 con
 * `Diagnostico__Ambiente=LOCAL|DEV` y `Diagnostico__Simulacion__Habilitada=true`, y esta app en 4024). Con la simulación de
 * RUNT (HU #13725) una placa terminada en 1 responde con éxito. Excluido de `tsc` y de Vitest; se corre con Playwright
 * desde la raíz del repo, por ejemplo:
 *
 *   DIAGNOSTICO_URL=http://127.0.0.1:4024 E2E_USUARIO=... E2E_CLAVE=... \
 *   pnpm --dir frontend exec playwright test ../frontend-diagnostico/e2e/evidencias.spec.ts
 *
 * E2E_USUARIO es un usuario de la compañía con diagnosticos.crear, diagnosticos.ver y reportes.descargar. Las credenciales
 * son de usuarios de prueba del ambiente DEV y entran solo por variables de entorno.
 *
 * Lista congelada (HU #14562 · AC55): nombres accesibles y atributos data-* de los que depende este spec.
 * Si uno cambia por decisión de UX, el spec se ajusta en el mismo PR de la pantalla.
 *   - button «/ingresar|iniciar sesión/i»
 *   - table «/semáforo de consultas/i»
 *   - button «Acciones de la placa ${PLACA}» · menu · menuitem «/^Descargar PDF/» (ola 2026-11-15, FE-2: el PDF pasó del
 *     enlace suelto «Descargar PDF de ${PLACA}» al menú «Acciones» de la fila; deshabilitado con motivo hasta que termina)
 *   - region «Descargas del lote»
 *   - link «Excel del lote»
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
/** Placa de prueba terminada en 1: la simulación responde con éxito. */
const PLACA = process.env.E2E_PLACA_EXPRESS ?? "TST101";

async function entrar(page: Page, usuario: string, clave: string) {
  // Ola 2026-11-15 (FE-4): «/» es el Dashboard; el listado y la tarjeta «Placa o VIN» viven en /diagnosticos.
  await page.goto(`${APP}/diagnosticos`);
  await page.waitForURL((url) => !url.href.startsWith(APP));
  await page.getByLabel(/correo|usuario/i).fill(usuario);
  await page.getByLabel("Contraseña", { exact: true }).fill(clave);
  await page.getByRole("button", { name: /ingresar|iniciar sesión/i }).click();
  await page.waitForURL(`${APP}/diagnosticos`);
}

test.describe("descargas del tablero", () => {
  test("AC2: express en verde → PDF del vehículo (application/pdf) y Excel del lote", async ({ page }) => {
    test.skip(!process.env.E2E_USUARIO || !process.env.E2E_CLAVE, "Faltan E2E_USUARIO / E2E_CLAVE");
    await entrar(page, process.env.E2E_USUARIO!, process.env.E2E_CLAVE!);

    // HU #14085: el inicio abre la pantalla única en Express con la placa precargada.
    await lanzarExpress(page, PLACA);

    const grilla = page.getByRole("table", { name: /semáforo de consultas/i });
    await expect(grilla).toBeVisible();
    // AC1: el PDF se habilita cuando el vehículo termina (todas sus celdas en verde o N/A). Ola 2026-11-15 (FE-2): el ítem
    // vive en el menú «Acciones» de la fila y, mientras no termina, sale con aria-disabled y su motivo; se reabre el menú
    // hasta que quede habilitado.
    const acciones = grilla.getByRole("button", { name: `Acciones de la placa ${PLACA}`, exact: true });
    const pdf = page.getByRole("menu").getByRole("menuitem", { name: /^Descargar PDF/ });
    await expect(async () => {
      if ((await acciones.getAttribute("aria-expanded")) !== "true") await acciones.click();
      await expect(pdf).toBeVisible({ timeout: 2_000 });
      if ((await pdf.getAttribute("aria-disabled")) === "true") {
        await page.keyboard.press("Escape");
        throw new Error("PDF aún deshabilitado");
      }
    }).toPass({ timeout: 90_000, intervals: [3_000] });

    const [respuestaPdf, descargaPdf] = await Promise.all([
      page.waitForResponse((r) => /\/vehiculos\/[^/]+\/pdf$/.test(new URL(r.url()).pathname)),
      page.waitForEvent("download"),
      pdf.click(),
    ]);
    expect(respuestaPdf.status()).toBe(200);
    expect(respuestaPdf.headers()["content-type"]).toContain("application/pdf");
    expect(descargaPdf.suggestedFilename()).toMatch(/^diagnostico-[0-9a-f]{8}-.+\.pdf$/i);

    const [respuestaExcel, descargaExcel] = await Promise.all([
      page.waitForResponse((r) => new URL(r.url()).pathname.endsWith("/excel")),
      page.waitForEvent("download"),
      page.getByRole("region", { name: "Descargas del lote" }).getByRole("link", { name: "Excel del lote" }).click(),
    ]);
    expect(respuestaExcel.status()).toBe(200);
    expect(respuestaExcel.headers()["content-type"]).toContain("spreadsheetml");
    expect(descargaExcel.suggestedFilename()).toMatch(/^diagnostico-[0-9a-f]{8}\.xlsx$/i);
  });
});
