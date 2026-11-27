import fs from "node:fs";
import { expect, test, type Page } from "@playwright/test";
import { ADMIN, entrar, esperarChip, exigir, exigirStub, liquidarPlaca, placaFicticia, responder, restaurarStub, revisarTemas, rutaApi } from "./comun";

/**
 * HU #14694 · Recibos PDF por proxy (plan §1.6, §2.4, §5.1 M11; spec-visual §1.4 y §3.6; contrato 1.2.1).
 * Criterios: F2b-CF7 (PDF por proxy, nunca la URL del proveedor), estados del recibo (Descargando / Disponible / Sin recibo /
 * No disponible) y 409 RECIBO_NO_DISPONIBLE.
 *
 * Modo de la API: suite real + stub 8898. Placa ficticia ⇒ `consulta-ok-deuda` (recibos 2025, 2024 y consolidado); PAZ000 ⇒
 * paz y salvo; FMS000 ⇒ el stub responde 404 `file_missing` a la descarga y M11 deja el recibo SIN_ARCHIVO con motivo
 * FILE_MISSING (sin botón). `page.route` solo en TC04: un recibo GUARDADO cuya descarga responde 409 RECIBO_NO_DISPONIBLE
 * (el archivo se perdió después de guardarlo), que la suite no produce a voluntad.
 *
 * Lista de nombres accesibles (ListaRecibos de M16; ajustar en la Fase 3 si cambian):
 *   - region «Recibos» · texto «Recibo de vigencia 2025», «Recibo consolidado», «Paz y salvo» · chip «Disponible» /
 *     «Sin recibo» / «Descargando» · button «Descargar recibo: <nombre>» · alert «El recibo no está disponible.»
 */
test.describe.configure({ mode: "serial" });

const recibos = (page: Page) => page.getByRole("region", { name: "Recibos" });
const botonDescarga = (page: Page, nombre: string | RegExp) =>
  recibos(page).getByRole("button", { name: typeof nombre === "string" ? `Descargar recibo: ${nombre}` : nombre });

/** M11 descarga los PDF después del resultado: espera a que el botón del recibo aparezca (la página se sondea sola). */
async function esperarDisponible(page: Page, nombre: string) {
  await expect(async () => {
    await page.reload();
    await expect(botonDescarga(page, nombre)).toBeVisible({ timeout: 3_000 });
  }).toPass({ timeout: 120_000, intervals: [5_000] });
}

test.describe("HU #14694 recibos PDF", () => {
  test.beforeEach(async ({ page, request }) => {
    exigir(ADMIN, "Administrador");
    await exigirStub(request);
    await entrar(page, ADMIN, "/liquidaciones");
  });
  test.afterEach(async ({ request }) => {
    await restaurarStub(request);
  });

  test("QA_TC01_IMPUESTOS_Recibos - Descargar el recibo de vigencia como PDF por el proxy", async ({ page }) => {
    await liquidarPlaca(page, placaFicticia());
    await esperarChip(page, "Con deuda");
    await esperarDisponible(page, "Recibo de vigencia 2025");
    await expect(recibos(page).getByText("Recibo consolidado")).toBeVisible();
    await expect(recibos(page).getByText("Disponible", { exact: true }).first()).toBeVisible();

    const peticion = page.waitForRequest(rutaApi("/liquidaciones/{id}/items/{id}/recibos/{id}"));
    const descarga = page.waitForEvent("download");
    await botonDescarga(page, "Recibo de vigencia 2025").click();
    expect(new URL((await peticion).url()).host, "el PDF sale del BFF de Impuestos, no del proveedor").toBe(new URL(page.url()).host);
    const archivo = await descarga;
    expect(archivo.suggestedFilename()).toMatch(/\.pdf$/i);
    const ruta = await archivo.path();
    expect(fs.readFileSync(ruta).subarray(0, 5).toString("latin1")).toBe("%PDF-");
    await revisarTemas(page, "recibos", "RC-1");
  });

  test("QA_TC02_IMPUESTOS_Recibos - Paz y salvo con su recibo descargable", async ({ page }) => {
    await liquidarPlaca(page, "PAZ000");
    await esperarChip(page, "Paz y salvo");
    await esperarDisponible(page, "Paz y salvo");
  });

  test("QA_TC03_IMPUESTOS_Recibos - Archivo inexistente en el proveedor queda Sin recibo y sin botón", async ({ page }) => {
    await liquidarPlaca(page, "FMS000");
    await esperarChip(page, "Con deuda");
    await expect(async () => {
      await page.reload();
      await expect(recibos(page).getByText("El archivo no existe en el proveedor").first()).toBeVisible({ timeout: 3_000 });
    }).toPass({ timeout: 120_000, intervals: [5_000] });
    await expect(recibos(page).getByText("Sin recibo", { exact: true }).first()).toBeVisible();
    await expect(botonDescarga(page, /^Descargar recibo/)).toHaveCount(0);
  });

  test("QA_TC04_IMPUESTOS_Recibos - Descarga con RECIBO_NO_DISPONIBLE muestra el literal del contrato", async ({ page }) => {
    await liquidarPlaca(page, placaFicticia());
    await esperarChip(page, "Con deuda");
    await esperarDisponible(page, "Recibo de vigencia 2024");
    // page.route: la descarga real respondería el PDF; se fuerza el 409 del contrato.
    await responder(page, rutaApi("/liquidaciones/{id}/items/{id}/recibos/{id}"), 409, { code: "RECIBO_NO_DISPONIBLE", error: "El recibo no está disponible." }, "GET");
    await botonDescarga(page, "Recibo de vigencia 2024").click();
    await expect(recibos(page).getByRole("alert").filter({ hasText: "El recibo no está disponible." })).toBeVisible();
    await expect(botonDescarga(page, "Recibo de vigencia 2024")).toHaveAttribute("aria-describedby", /.+/);
  });
});
