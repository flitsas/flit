import { expect, test, type Page } from "@playwright/test";
import {
  ADMIN,
  APP,
  alcanzarConTab,
  csvPlacas,
  entrar,
  exigir,
  exigirStub,
  idsDeUrl,
  placaFicticia,
  placasUnicas,
  registrarPeticiones,
  restaurarStub,
  revisar360,
  revisarTemas,
  rutaApi,
} from "./comun";

/**
 * HU #14694 / #14695 · Liquidación masiva (plan §2.3, §3.3, §5.1, §5.9; spec-visual §3.2, §3.4 y §3.5; contrato 1.2.1).
 * Criterios: F3-CF1 (CSV/XLSX, duplicadas e inválidas), F3-CF2 (tope antes de crear: LIMITE_SUPERADO), F3-CF5/CF6 (avance y
 * grid mixto con pestañas), F3-CF12 + DA-6b (cierre manual con motivo; delta 5 `motivoCierre`).
 *
 * Modo de la API: suite real + stub 8898, sin `page.route`. El análisis no consulta el RUNT ni el proveedor. Para que el lote
 * siga abierto al cerrarlo se incluye la placa mágica TMO000 (el stub registra la consulta y corta sin responder: el ítem
 * queda «Incierto» hasta el plazo de conciliación). El tope se lee del propio análisis (aria-valuemax de la barra).
 *
 * Lista de nombres accesibles (componentes de M16; ajustar en la Fase 3 si cambian):
 *   - h1 «Liquidación masiva» · list «Pasos de la carga» · file input «Archivo de placas (.xlsx o .csv)» · button «Analizar
 *     archivo» · button «Descargar plantilla» · list «Resultado del análisis» («Válidas: N», «Duplicadas (se liquidan una
 *     vez): N», «Inválidas: N») · progressbar «Placas válidas frente al máximo del lote» · table «Filas inválidas» · alert
 *     «Divide el archivo para continuar» · button «Iniciar liquidación de N placas» · button «Subir otro archivo»
 *   - detalle del lote: chip de estado · button «Cerrar lote» · dialog «Cerrar lote» · textbox «Motivo (opcional)» ·
 *     progressbar «Avance del lote» · group «Filtros del lote» · table «Placas del lote»
 *   - /liquidaciones: tablist «Tipo de liquidación» · tab «Todas», «Masivas», «Por placa» · table «Liquidaciones» · link con
 *     los 8 primeros caracteres del id en mayúsculas
 */
test.describe.configure({ mode: "serial" });

const ARCHIVO = "Archivo de placas (.xlsx o .csv)";

async function analizar(page: Page, placas: readonly string[]) {
  await page.getByLabel(ARCHIVO).setInputFiles(csvPlacas(placas));
  await page.getByRole("button", { name: "Analizar archivo" }).click();
  await expect(page.getByRole("list", { name: "Resultado del análisis" })).toBeVisible({ timeout: 30_000 });
}

/** Crea un lote con las placas dadas y devuelve su id (la app navega al detalle del lote). */
async function crearLote(page: Page, placas: readonly string[]): Promise<string> {
  await page.goto(`${APP}/liquidaciones/masiva`);
  await analizar(page, placas);
  await page.getByRole("button", { name: `Iniciar liquidación de ${placas.length} placas` }).click();
  await page.waitForURL(/\/liquidaciones\/[0-9a-f-]{36}(\?|$)/i);
  await expect(page.getByRole("heading", { level: 1, name: "Liquidación masiva" })).toBeVisible();
  return idsDeUrl(page.url()).liquidacionId;
}

const idCorto = (id: string) => id.slice(0, 8).toUpperCase();

test.describe("HU #14694 liquidación masiva (Administrador)", () => {
  test.beforeEach(async ({ page, request }) => {
    exigir(ADMIN, "Administrador");
    await exigirStub(request);
    await entrar(page, ADMIN, "/liquidaciones/masiva");
  });
  test.afterEach(async ({ request }) => {
    await restaurarStub(request);
  });

  test("QA_TC01_IMPUESTOS_LiquidacionMasiva - Descargar la plantilla xlsx", async ({ page }) => {
    const descarga = page.waitForEvent("download");
    await page.getByRole("button", { name: "Descargar plantilla" }).click();
    const archivo = await descarga;
    expect(archivo.suggestedFilename()).toMatch(/\.xlsx$/);
    expect(await archivo.failure()).toBeNull();
  });

  test("QA_TC02_IMPUESTOS_LiquidacionMasiva - Analizar CSV con duplicadas e inválidas antes de crear", async ({ page }) => {
    const creaciones = registrarPeticiones(page, rutaApi("/liquidaciones/masiva"));
    const [a, b] = [placaFicticia(), placaFicticia()];
    await analizar(page, [a, b, a.toLowerCase(), "AB"]);
    const resumen = page.getByRole("list", { name: "Resultado del análisis" });
    await expect(resumen.getByText("Válidas: 2")).toBeVisible();
    await expect(resumen.getByText("Duplicadas (se liquidan una vez): 1")).toBeVisible();
    await expect(resumen.getByText("Inválidas: 1")).toBeVisible();
    const invalidas = page.getByRole("table", { name: "Filas inválidas" });
    const fila = invalidas.getByRole("row").filter({ hasText: "AB" });
    await expect(fila.getByRole("cell", { name: "Placa no válida (de 4 a 10 letras o números)" })).toBeVisible();
    await expect(fila.getByRole("cell", { name: "5", exact: true })).toBeVisible(); // fila 5 (la 1 es la cabecera)
    await expect(page.getByRole("button", { name: "Iniciar liquidación de 2 placas" })).toBeEnabled();
    expect(creaciones.filter((u) => !u.pathname.endsWith("/analizar")).length, "el análisis no crea el lote").toBe(0);
    await revisarTemas(page, "analisis-masiva", "LM-1");
    await revisar360(page, "analisis-masiva", "LM-1");
  });

  test("QA_TC03_IMPUESTOS_LiquidacionMasiva - Archivo que supera el tope no deja crear el lote", async ({ page }) => {
    await analizar(page, [placaFicticia()]);
    const tope = Number(await page.getByRole("progressbar", { name: "Placas válidas frente al máximo del lote" }).getAttribute("aria-valuemax"));
    expect(tope, "tope del lote (contractual ∩ operativa)").toBeGreaterThan(0);
    await page.getByRole("button", { name: "Subir otro archivo" }).click();
    await analizar(page, placasUnicas(tope + 1));
    await expect(page.getByText("Divide el archivo para continuar")).toBeVisible();
    await expect(page.getByText(`El archivo supera el tope de ${tope} placas por lote.`)).toBeVisible();
    await expect(page.getByRole("button", { name: `Iniciar liquidación de ${tope + 1} placas` })).toBeDisabled();
  });

  test("QA_TC04_IMPUESTOS_LiquidacionMasiva - Crear el lote y cerrarlo a mano con motivo", async ({ page }) => {
    const id = await crearLote(page, [placaFicticia(), "TMO000"]);
    await expect(page.getByRole("progressbar", { name: "Avance del lote" })).toBeVisible();
    await expect(page.getByRole("table", { name: "Placas del lote" }).or(page.getByText("Este lote todavía no tiene placas en proceso"))).toBeVisible();

    const cerrar = page.getByRole("button", { name: "Cerrar lote" });
    await alcanzarConTab(page, cerrar);
    await page.keyboard.press("Enter");
    const dialogo = page.getByRole("dialog", { name: "Cerrar lote" });
    await expect(dialogo).toBeVisible();
    await expect(dialogo.getByText("Cerrar el lote deja las placas pendientes en «Sin resultado (lote cerrado)». Si después llega un resultado, se aplica a la placa sin reabrir el lote.")).toBeVisible();
    await expect(dialogo.getByRole("button", { name: "Cancelar" })).toBeFocused();
    await dialogo.getByRole("textbox", { name: "Motivo (opcional)" }).fill("Cierre de prueba E2E");
    await revisarTemas(page, "dialogo-cerrar-lote", "LM-2");
    await dialogo.getByRole("button", { name: "Cerrar lote" }).click();

    await expect(dialogo).toHaveCount(0);
    await expect(page.getByText("Cerrado manualmente", { exact: true }).first()).toBeVisible();
    await expect(page.getByText(/^Cerrado manualmente el \d{2}\/\d{2}\/\d{4}/)).toBeVisible();
    await expect(page.getByText("Motivo: Cierre de prueba E2E")).toBeVisible();
    await expect(page.getByRole("button", { name: "Cerrar lote" })).toHaveCount(0);
    // TMO000 no terminó: queda «Sin resultado (lote cerrado)» (o con su resultado tardío si llegó después).
    await expect(page.getByRole("table", { name: "Placas del lote" }).getByRole("row").filter({ hasText: "TMO000" })).toContainText(/Sin resultado \(lote cerrado\)|Con deuda|Con error/);
    expect(id).toMatch(/^[0-9a-f-]{36}$/i);
  });

  test("QA_TC05_IMPUESTOS_LiquidacionMasiva - Pestañas Masivas y Por placa del grid mixto", async ({ page }) => {
    const id = await crearLote(page, [placaFicticia()]);
    await page.goto(`${APP}/liquidaciones`);
    const pestanas = page.getByRole("tablist", { name: "Tipo de liquidación" });
    await expect(pestanas.getByRole("tab", { name: "Todas" })).toHaveAttribute("aria-selected", "true");
    const tabla = page.getByRole("table", { name: "Liquidaciones" });
    await expect(tabla.getByRole("columnheader", { name: "Tipo", exact: true })).toBeVisible();
    await expect(tabla.getByRole("link", { name: idCorto(id) })).toBeVisible();

    await pestanas.getByRole("tab", { name: "Masivas" }).click();
    await expect(page).toHaveURL(/[?&]tipo=MASIVA/);
    await expect(pestanas.getByRole("tab", { name: "Masivas" })).toHaveAttribute("aria-selected", "true");
    await expect(tabla.getByRole("columnheader", { name: "Tipo", exact: true })).toHaveCount(0);
    await expect(tabla.getByRole("link", { name: idCorto(id) })).toBeVisible();

    await pestanas.getByRole("tab", { name: "Por placa" }).click();
    await expect(page).toHaveURL(/[?&]tipo=INDIVIDUAL/);
    await expect(page.getByRole("table", { name: "Liquidaciones" }).or(page.getByRole("heading", { name: /Ninguna liquidación coincide|Aún no hay liquidaciones/ }))).toBeVisible();
    await expect(page.getByRole("link", { name: idCorto(id) })).toHaveCount(0);
    await revisarTemas(page, "grid-liquidaciones", "LM-3");
    await revisar360(page, "grid-liquidaciones", "LM-3");
  });
});
