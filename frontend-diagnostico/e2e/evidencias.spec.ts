import { expect, test, type Page } from "@playwright/test";

/**
 * HU #13851 (F07-H6) — descargas del tablero: PDF del vehículo y Excel del lote.
 *
 * Artefacto E2E para QA: necesita la suite levantada (hub en 4022, gateway en 4002, core-diagnostico en 4029 con
 * `Diagnostico__Ambiente=LOCAL|DEV` y `Diagnostico__Simulacion__Habilitada=true`, y esta app en 4024). Con la simulación de
 * RUNT (HU #13725) una placa terminada en 1 responde con éxito. Excluido de `tsc` y de Vitest; se corre con Playwright
 * desde la raíz del repo, por ejemplo:
 *
 *   DIAGNOSTICO_URL=http://127.0.0.1:4024 E2E_USUARIO=... E2E_CLAVE=... \
 *   pnpm --dir frontend exec playwright test ../frontend-diagnostico/e2e/evidencias.spec.ts
 *
 * E2E_USUARIO es un usuario de la compañía con diagnosticos.crear, diagnosticos.ver y reportes.descargar. Las credenciales
 * son de usuarios de prueba del ambiente DEV y entran solo por variables de entorno.
 */
const APP = process.env.DIAGNOSTICO_URL ?? "http://127.0.0.1:4024";
/** Placa de prueba terminada en 1: la simulación responde con éxito. */
const PLACA = process.env.E2E_PLACA_EXPRESS ?? "TST101";

async function entrar(page: Page, usuario: string, clave: string) {
  await page.goto(`${APP}/`);
  await page.waitForURL((url) => !url.href.startsWith(APP));
  await page.getByLabel(/correo|usuario/i).fill(usuario);
  await page.getByLabel(/contraseña/i).fill(clave);
  await page.getByRole("button", { name: /ingresar|iniciar sesión/i }).click();
  await page.waitForURL(`${APP}/`);
}

test.describe("descargas del tablero", () => {
  test("AC2: express en verde → PDF del vehículo (application/pdf) y Excel del lote", async ({ page }) => {
    test.skip(!process.env.E2E_USUARIO || !process.env.E2E_CLAVE, "Faltan E2E_USUARIO / E2E_CLAVE");
    await entrar(page, process.env.E2E_USUARIO!, process.env.E2E_CLAVE!);

    await page.getByLabel(/placa o vin/i).fill(PLACA);
    await page.getByRole("button", { name: "Diagnosticar" }).click();
    await page.getByRole("button", { name: "Lanzar diagnóstico" }).click();
    await page.waitForURL(/\/diagnosticos\/[0-9a-f-]{36}$/i);

    const grilla = page.getByRole("table", { name: /semáforo de consultas/i });
    await expect(grilla).toBeVisible();
    // AC1: el PDF se habilita cuando el vehículo termina (todas sus celdas en verde o N/A).
    const pdf = grilla.getByRole("link", { name: `Descargar PDF de ${PLACA}` });
    await expect(pdf).toBeVisible({ timeout: 90_000 });

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
      page.getByRole("region", { name: "Descargas del lote" }).getByRole("link", { name: "Descargar Excel" }).click(),
    ]);
    expect(respuestaExcel.status()).toBe(200);
    expect(respuestaExcel.headers()["content-type"]).toContain("spreadsheetml");
    expect(descargaExcel.suggestedFilename()).toMatch(/^diagnostico-[0-9a-f]{8}\.xlsx$/i);
  });
});
