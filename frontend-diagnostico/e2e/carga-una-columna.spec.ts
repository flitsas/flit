import { expect, test, type Locator, type Page } from "@playwright/test";
import { APP, CLIENTE, entrar, exigirCliente, placaFicticia, prefijoFicticio, sinDesbordeHorizontal } from "./saneamiento-comun";
import { revisarTemasOla } from "./ola-ux-comun";

/**
 * FE-3 · Carga express/masiva en una columna con Parámetros arriba en horizontal (spec-ui §3).
 *
 * Gherkin (particion-hus.md FE-3): «una columna» y «foco al primer inválido»; además el envío Express sigue funcionando
 * (con Enter desde «Nombre del diagnóstico», spec-ui §3.4.4) y el Masivo valida el archivo desde la barra de acción.
 *
 * Modo de la API: suite local real con simulación; placas ficticias del prefijo de la corrida. Sin `page.route`.
 * Variables: E2E_USUARIO / E2E_CLAVE (compañía con diagnosticos.crear).
 *
 * Lista congelada:
 *   - region «Parámetros» · region «Vehículos» · region «Información general» · group «Modo» · group «Alcance»
 *   - textbox «Nombre del diagnóstico» · textbox «Placa o VIN» · button «/^(Continuar|Iniciar diagnóstico)$/»
 *   - button «Validar archivo» · button «Lanzar diagnóstico» · region «Resumen de validación» · etiqueta «/archivo de vehículos/i»
 *   - radio «Express» · radio «Masivo» · checkbox «RUNT vehículo»
 * Barra de acción (S5 de diseno.md): la CTA va DESPUÉS de Vehículos y fuera de la región «Parámetros» (`form=` asociado).
 */
test.describe.configure({ mode: "serial" });

const MODOS = ["express", "masivo"] as const;
const parametros = (page: Page) => page.getByRole("region", { name: "Parámetros" });
const vehiculos = (page: Page) => page.getByRole("region", { name: "Vehículos" });
const secciones = (page: Page) => ({
  info: parametros(page).getByRole("region", { name: "Información general" }),
  modo: parametros(page).getByRole("group", { name: "Modo", exact: true }),
  alcance: parametros(page).getByRole("group", { name: "Alcance", exact: true }),
});
const cta = (page: Page, modo: (typeof MODOS)[number]) =>
  modo === "masivo" ? page.getByRole("button", { name: "Validar archivo" }) : page.getByRole("button", { name: /^(Continuar|Iniciar diagnóstico)$/ });

async function caja(l: Locator) {
  await expect(l).toBeVisible();
  const c = await l.boundingBox();
  expect(c, "bounding box").toBeTruthy();
  return c!;
}

/** `a` va antes que `b` en el documento (orden DOM = orden visual, spec-ui §3.4.3). */
async function antesEnDom(a: Locator, b: Locator): Promise<boolean> {
  const hb = await b.elementHandle();
  return a.evaluate((x, y) => Boolean(x.compareDocumentPosition(y as Node) & Node.DOCUMENT_POSITION_FOLLOWING), hb);
}

async function irANuevo(page: Page, modo: (typeof MODOS)[number], ancho: number) {
  await page.setViewportSize({ width: ancho, height: 900 });
  await page.goto(`${APP}/diagnosticos/nuevo?modo=${modo}`);
  await expect(page.getByRole("radio", { name: modo === "express" ? "Express" : "Masivo" })).toHaveAttribute("aria-checked", "true");
  // El alcance carga la configuración de la compañía: RUNT vehículo siempre se ofrece.
  await expect(page.getByRole("checkbox", { name: "RUNT vehículo" })).toBeVisible();
}

test.describe("FE-3 carga en una columna", () => {
  test.beforeEach(async ({ page }) => {
    exigirCliente();
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, "/diagnosticos");
  });

  for (const modo of MODOS) {
    test(`${modo}: Parámetros arriba de Vehículos a ancho completo, sin sticky; en 1280 px las 3 secciones en fila`, async ({ page }) => {
      await irANuevo(page, modo, 1280);
      const [p, v] = [await caja(parametros(page)), await caja(vehiculos(page))];
      expect(p.y + p.height, "Parámetros termina antes de que empiece Vehículos").toBeLessThanOrEqual(v.y + 1);
      expect(Math.abs(p.width - v.width), "Parámetros y Vehículos con el mismo ancho").toBeLessThanOrEqual(2);
      expect(Math.abs(p.x - v.x)).toBeLessThanOrEqual(2);
      expect(await parametros(page).evaluate((el) => getComputedStyle(el).position)).not.toBe("sticky");

      // ≥ 1280 (xl): Información general · Modo · Alcance lado a lado, en ese orden, y Alcance la más ancha (2fr).
      const s = secciones(page);
      const [i, m, a] = [await caja(s.info), await caja(s.modo), await caja(s.alcance)];
      expect(Math.abs(i.y - m.y), "Info y Modo en la misma fila").toBeLessThanOrEqual(8);
      expect(Math.abs(m.y - a.y), "Modo y Alcance en la misma fila").toBeLessThanOrEqual(8);
      expect(i.x).toBeLessThan(m.x);
      expect(m.x).toBeLessThan(a.x);
      expect(a.width).toBeGreaterThan(m.width);

      // Barra de acción al final, después de Vehículos y fuera de Parámetros.
      const boton = cta(page, modo);
      const b = await caja(boton);
      expect(b.y, "la CTA va debajo de Vehículos").toBeGreaterThanOrEqual(v.y + v.height - 1);
      await expect(parametros(page).getByRole("button", { name: /^(Continuar|Iniciar diagnóstico|Validar archivo)$/ })).toHaveCount(0);

      // Orden DOM = orden visual: Info → Modo → Alcance → Vehículos → barra.
      expect(await antesEnDom(s.info, s.modo)).toBe(true);
      expect(await antesEnDom(s.modo, s.alcance)).toBe(true);
      expect(await antesEnDom(s.alcance, vehiculos(page))).toBe(true);
      expect(await antesEnDom(vehiculos(page), boton)).toBe(true);

      await sinDesbordeHorizontal(page, `carga-${modo}-1280`);
      await revisarTemasOla(page, `carga-${modo}-1280`, { captura: "C-1" });
    });

    test(`${modo}: en 1024 px Info y Modo arriba y Alcance debajo; en 375 px todo apilado sin scroll horizontal`, async ({ page }) => {
      await irANuevo(page, modo, 1024);
      let s = secciones(page);
      let [i, m, a] = [await caja(s.info), await caja(s.modo), await caja(s.alcance)];
      expect(Math.abs(i.y - m.y), "1024: Info y Modo en la misma fila").toBeLessThanOrEqual(8);
      expect(a.y, "1024: Alcance debajo").toBeGreaterThanOrEqual(Math.max(i.y + i.height, m.y + m.height) - 1);
      await sinDesbordeHorizontal(page, `carga-${modo}-1024`);
      await revisarTemasOla(page, `carga-${modo}-1024`, { captura: "C-1" });

      await irANuevo(page, modo, 375);
      s = secciones(page);
      [i, m, a] = [await caja(s.info), await caja(s.modo), await caja(s.alcance)];
      const v = await caja(vehiculos(page));
      const b = await caja(cta(page, modo));
      const pila = [i, m, a, v, b];
      for (let k = 1; k < pila.length; k++) {
        expect(pila[k].y, `375: el bloque ${k} va debajo del ${k - 1}`).toBeGreaterThanOrEqual(pila[k - 1].y + pila[k - 1].height - 1);
      }
      await sinDesbordeHorizontal(page, `carga-${modo}-375`);
      await revisarTemasOla(page, `carga-${modo}-375`, { captura: "C-1" });
    });
  }

  test("foco al primer inválido: con el nombre vacío y sin vehículos, el foco va a «Nombre del diagnóstico»", async ({ page }) => {
    await irANuevo(page, "express", 1280);
    const nombre = page.getByRole("textbox", { name: "Nombre del diagnóstico" });
    await nombre.fill("");
    await page.getByRole("textbox", { name: "Placa o VIN" }).fill("");
    await cta(page, "express").click();
    await expect(nombre).toBeFocused();
    await expect(nombre).toHaveAttribute("aria-invalid", "true");
    await expect(nombre).toHaveAccessibleDescription(/\S/);
    await revisarTemasOla(page, "carga-express-error-1280", { captura: "C-2" });
  });

  test("masivo: «Validar archivo» desde la barra crea la cabecera, analiza y muestra el resumen a ancho completo", async ({ page }) => {
    const placa = placaFicticia(prefijoFicticio(), "1");
    await irANuevo(page, "masivo", 1280);
    await page.getByRole("textbox", { name: "Nombre del diagnóstico" }).fill(`E2E una columna masivo ${Date.now()}`);
    await page.getByLabel(/archivo de vehículos/i).setInputFiles({
      name: "lote-una-columna.csv",
      mimeType: "text/csv",
      buffer: Buffer.from(`Identificador_Vehiculo\n${placa}\n`),
    });
    const [crear, analizar] = await Promise.all([
      page.waitForResponse((r) => r.request().method() === "POST" && new URL(r.url()).pathname.endsWith("/diagnosticos")),
      page.waitForResponse((r) => r.request().method() === "POST" && new URL(r.url()).pathname.endsWith("/carga")),
      cta(page, "masivo").click(),
    ]);
    expect(crear.status()).toBe(201);
    expect(analizar.status()).toBe(200);
    const resumen = page.getByRole("region", { name: "Resumen de validación" });
    const [r, v] = [await caja(resumen), await caja(vehiculos(page))];
    expect(r.y, "el resumen va después de Vehículos").toBeGreaterThanOrEqual(v.y + v.height - 1);
    expect(Math.abs(r.width - v.width), "el resumen a ancho completo").toBeLessThanOrEqual(2);
    await revisarTemasOla(page, "carga-masivo-resumen-1280", { captura: "C-3" });
  });

  test("express: Enter en «Nombre del diagnóstico» envía el formulario y el Express llega al tablero", async ({ page }) => {
    const placa = placaFicticia(prefijoFicticio(), "1");
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto(`${APP}/diagnosticos/nuevo?modo=express&placa=${encodeURIComponent(placa)}`);
    await expect(page.getByRole("textbox", { name: "Placa o VIN" })).toHaveValue(placa);
    const nombre = page.getByRole("textbox", { name: "Nombre del diagnóstico" });
    await nombre.fill(`E2E una columna express ${Date.now()}`);
    await nombre.press("Enter");
    const tablero = /\/diagnosticos\/[0-9a-f-]{36}$/i;
    const lanzar = page.getByRole("button", { name: "Lanzar diagnóstico" });
    const verTablero = page.getByRole("link", { name: "Ver tablero" });
    await Promise.race([lanzar.waitFor({ state: "visible" }), verTablero.waitFor({ state: "visible" }), page.waitForURL(tablero, { waitUntil: "commit" })]);
    if (await lanzar.isVisible()) await lanzar.click();
    // La pantalla de resultado redirige sola; se navega al href para no competir con ella (mismo patrón que express.ts).
    if (!tablero.test(new URL(page.url()).pathname)) {
      await verTablero.waitFor({ state: "visible" }).catch(() => undefined);
      const href = await verTablero.getAttribute("href", { timeout: 2_000 }).catch(() => null);
      if (href && !tablero.test(new URL(page.url()).pathname)) await page.goto(new URL(href, APP).href, { waitUntil: "commit" }).catch(() => undefined);
    }
    await page.waitForURL(tablero, { waitUntil: "commit" });
    await expect(page.getByRole("table", { name: /semáforo de consultas/i })).toBeVisible();
  });
});
