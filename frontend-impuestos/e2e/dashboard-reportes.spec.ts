import { expect, test } from "@playwright/test";
import { ADMIN, APP, entrar, esperarChip, exigir, exigirStub, liquidarPlaca, placaFicticia, registrarPeticiones, reescribir, revisar360, revisarTemas, rutaApi } from "./comun";

/**
 * HU #14695 · Humo de Dashboard y Reportes (plan §5.1, §5.7 deltas 9 y 13; spec-visual §3.1 y §3.7; contrato 1.2.1).
 * Criterios: F4a-CF1 (dashboard y rango), F4a-CF3 (monto null = «Sin dato», nunca $ 0), F4b-CF1 (4 reportes, filtros y
 * exportación con los mismos filtros del GET, delta 9).
 *
 * Modo de la API: suite real. Cada test liquida antes una placa ficticia para no depender de otros specs. `page.route` solo
 * en TC01: `Dashboard.montoTotal` null y `liquidacionesSinMonto` 0 (delta 13), que la suite no produce con resultados reales.
 *
 * Lista de nombres accesibles (M17; ajustar en la Fase 3 si cambian):
 *   - h1 «Dashboard» · group «Periodo» · region «Monto total liquidado»
 *   - h1 «Reportes» · tablist «Reportes» · tab «Detalle de liquidaciones» · search «Filtros de Detalle de liquidaciones» ·
 *     textbox «Placa» · button «Exportar Excel» / «Exportar PDF»
 */
test.describe.configure({ mode: "serial" });

test.describe("HU #14695 dashboard y reportes (Administrador)", () => {
  test.beforeEach(async ({ page, request }) => {
    exigir(ADMIN, "Administrador");
    await exigirStub(request);
    await entrar(page, ADMIN, "/liquidaciones");
  });

  test("QA_TC01_IMPUESTOS_Dashboard - Monto total sin dato nunca se pinta como cero", async ({ page }) => {
    await liquidarPlaca(page, placaFicticia());
    await reescribir<Record<string, unknown>>(page, rutaApi("/dashboard"), (real) => ({ ...real, montoTotal: null, liquidacionesSinMonto: 0, parcial: false }));
    await page.goto(`${APP}/`);
    await expect(page.getByRole("heading", { level: 1, name: "Dashboard" })).toBeVisible();
    await expect(page.getByRole("group", { name: "Periodo" })).toBeVisible();
    const monto = page.getByRole("region", { name: "Monto total liquidado" });
    await expect(monto.getByText("Sin dato", { exact: true })).toBeVisible();
    await expect(monto).not.toContainText("$ 0");
    await revisarTemas(page, "dashboard", "DR-1");
    await revisar360(page, "dashboard", "DR-1");
  });

  test("QA_TC02_IMPUESTOS_Reportes - Exportar Excel con los mismos filtros del reporte en pantalla", async ({ page }) => {
    const placa = placaFicticia();
    await liquidarPlaca(page, placa);
    await esperarChip(page, "Con deuda");
    const lecturas = registrarPeticiones(page, /\/api\/v1\/impuestos\/reportes\/detalle(\?|$)/);
    const exportes = registrarPeticiones(page, /\/api\/v1\/impuestos\/reportes\/detalle\/excel(\?|$)/);
    await page.goto(`${APP}/reportes`);
    await expect(page.getByRole("heading", { level: 1, name: "Reportes" })).toBeVisible();
    await expect(page.getByRole("tab", { name: "Detalle de liquidaciones" })).toHaveAttribute("aria-selected", "true");

    const filtros = page.getByRole("search", { name: "Filtros de Detalle de liquidaciones" });
    const campo = filtros.getByRole("textbox", { name: "Placa" });
    await campo.fill(placa);
    await campo.press("Enter");
    await expect.poll(() => lecturas.at(-1)?.searchParams.get("placa")).toBe(placa);
    await expect(page.getByRole("table").getByText(placa).first()).toBeVisible();

    const descarga = page.waitForEvent("download");
    await page.getByRole("button", { name: "Exportar Excel" }).click();
    const archivo = await descarga;
    expect(archivo.suggestedFilename()).toMatch(/\.xlsx$/);

    const sinPagina = (u: URL) => {
      const p = new URLSearchParams(u.search);
      p.delete("page");
      p.delete("pageSize");
      return [...p.entries()].sort(([a], [b]) => a.localeCompare(b));
    };
    expect(exportes.length, "GET /reportes/detalle/excel").toBe(1);
    expect(sinPagina(exportes[0]), "la exportación lleva exactamente los filtros del GET (delta 9)").toEqual(sinPagina(lecturas.at(-1)!));
    await revisarTemas(page, "reportes", "DR-2");
  });
});
