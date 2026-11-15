import { expect, test, type Page } from "@playwright/test";
import {
  API,
  APP,
  BACKOFFICE,
  CLIENTE,
  EMPRESA,
  ancho,
  entrar,
  exigirBackoffice,
  exigirCliente,
  fallarUnaVez,
  placaFicticia,
  prefijoFicticio,
  retener,
  sembrarExpress,
  sinDesbordeHorizontal,
} from "./saneamiento-comun";
import {
  NOMBRE_EXPORTACION,
  RUTA,
  clavesAlmacenamiento,
  hoyBogota,
  leerSesion,
  reescribir,
  registrarPeticiones,
  responder,
  revisarTemasOla,
  sumarDias,
  variarCapacidades,
} from "./ola-ux-comun";

/**
 * FE-6 · Módulo de reportes: catálogo en pestañas, aviso de datos personales, tabla paginada, filtros y exportación Excel/PDF
 * (spec-ui §5; contrato-api.md §2–§6; decisiones U4, U5 (PII completa), O8 (diagnostico.reportes.ver) y O10).
 *
 * Gherkin (particion-hus.md FE-6): «catálogo y tabla genérica», «ámbito del super admin», «descargas», «PII en el front (SP-18)».
 *
 * Modo de la API: suite local real. El primer test siembra un Express con una placa ficticia (prefijo de la corrida) y filtra
 * por ese prefijo para que el reporte quede pequeño y por debajo de los máximos. `page.route` solo para forzar: cargando
 * (respuesta real retenida), vacío (página del contrato con 0 filas), error 500, `total` por encima del máximo (respuesta real
 * reescrita), 422 REPORTE_DEMASIADO_GRANDE, 429 EXPORTACION_EN_CURSO y la sesión sin `descargarReportes`.
 * Variables: E2E_USUARIO / E2E_CLAVE (compañía con diagnostico.reportes.ver y reportes.descargar) y
 * E2E_BACKOFFICE_USUARIO / E2E_BACKOFFICE_CLAVE / E2E_EMPRESA (SuperAdmin FLIT; uuid de una compañía habilitada).
 * Al correr este archivo con otros de la ola: `--workers=1` (sesión única por usuario).
 *
 * Divergencia abierta: spec-ui §5.4 dice tamaños 25/50/100; FE-6 dice que la paginación es la de `Pagination` (10/20/30/50,
 * fijos en packages/ui) y el contrato da 25 por defecto. El spec no fija la lista: comprueba que «Filas por página» cambia
 * `tamano` y que la página 2 pide `pagina=2`.
 *
 * Lista congelada (pantalla nueva de la Fase 2; ajustar en la Fase 3 si el nombre final cambia):
 *   - heading (h1) «Reportes» · texto «/datos personales/» y «Ley 1581» (aviso persistente) · texto «Cada descarga queda
 *     registrada con tu usuario.» · texto «Contiene datos personales» (junto a los botones)
 *   - tablist «Reportes disponibles» · tab «Vehículos diagnosticados», «Consultas a fuentes», «Saneamientos»,
 *     «Derivaciones a trámite», «Consumo por compañía» (solo backoffice)
 *   - etiqueta «Desde» · etiqueta «Hasta» · combobox «Compañía» · textbox «Placa» · button «Limpiar filtros»
 *   - table (DataTable) · columnheader con `columnas[].titulo` · texto «/\d registros?/»
 *   - combobox «Filas por página» · button «Página siguiente» · button «Página 2» (aria-current)
 *   - button «Exportar Excel» · button «Exportar PDF» · texto «Generando…» · texto «/Acota el filtro/» · texto «/espera/i»
 *   - texto «No hay registros en este periodo» · «Ningún registro coincide con los filtros» · button «Reintentar»
 */
test.describe.configure({ mode: "serial" });

const H1 = (page: Page) => page.getByRole("heading", { level: 1, name: "Reportes" });
const pestanas = (page: Page) => page.getByRole("tablist", { name: "Reportes disponibles" });
const excel = (page: Page) => page.getByRole("button", { name: /^(Exportar Excel|Generando…)/ }).first();
const pdf = (page: Page) => page.getByRole("button", { name: /^(Exportar PDF|Generando…)/ }).last();
const selectorCompania = (page: Page) => page.getByRole("combobox", { name: "Compañía" });
// El nombre accesible es la etiqueta visible del componente («Placa (inicio)», FiltrosReporte y su unit test); la spec-ui
// §5.3 no fija otro texto. Ronda 2 de E2E: el test buscaba «Placa» exacto, que el componente nunca pintó.
const filtroPlaca = (page: Page) => page.getByRole("textbox", { name: "Placa (inicio)", exact: true });

interface Catalogo {
  reportes: { tipo: string; nombre: string; contiene_pii: boolean; maximo_excel: number; maximo_pdf: number }[];
}
interface PaginaReporte {
  tipo: string;
  ambito: string;
  pagina: number;
  tamano: number;
  total: number;
  columnas: { clave: string; titulo: string; tipo: string }[];
  filas: Record<string, unknown>[];
}

/** Abre /reportes (con query) y devuelve la primera página que pide la tabla. */
async function abrirReportes(page: Page, ruta: RegExp, query = "") {
  const respuesta = page.waitForResponse((r) => r.request().method() === "GET" && ruta.test(r.url()));
  await page.goto(`${APP}/reportes${query}`);
  await expect(H1(page)).toBeVisible();
  return respuesta;
}

/** Descarga con el botón y comprueba nombre (sin PII) y tipo de contenido. */
async function exportar(page: Page, boton: ReturnType<typeof excel>, extension: "xlsx" | "pdf", noDebeContener: string[]) {
  const [respuesta, descarga] = await Promise.all([
    page.waitForResponse((r) => RUTA.exportacion.test(r.url()) && r.url().includes(`/${extension === "xlsx" ? "excel" : "pdf"}`)),
    page.waitForEvent("download"),
    boton.click(),
  ]);
  expect(respuesta.status()).toBe(200);
  expect(respuesta.headers()["content-type"]).toContain(extension === "xlsx" ? "spreadsheetml" : "application/pdf");
  expect(respuesta.headers()["content-disposition"]).toMatch(/attachment/);
  const nombre = descarga.suggestedFilename();
  expect(nombre).toMatch(NOMBRE_EXPORTACION);
  expect(nombre.endsWith(`.${extension}`)).toBe(true);
  for (const dato of noDebeContener) expect(nombre.toUpperCase(), "el nombre del archivo no lleva PII").not.toContain(dato.toUpperCase());
  // Exporta TODO el filtro: sin pagina ni tamano en la petición.
  const u = new URL(respuesta.url());
  expect(u.searchParams.has("pagina") || u.searchParams.has("tamano")).toBe(false);
  return { nombre, url: u };
}

test.describe("FE-6 reportes de la compañía", () => {
  let prefijo = "";

  test("catálogo en pestañas (sin «Consumo»), aviso PII persistente, tabla con las columnas del contrato y paginación", async ({ page }) => {
    exigirCliente();
    test.setTimeout(300_000);
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, "/diagnosticos");
    const sesion = await leerSesion(page);
    test.skip(!sesion.capacidades.verReportes, "E2E_USUARIO no tiene diagnostico.reportes.ver (verReportes)");
    prefijo = prefijoFicticio();
    await sembrarExpress(page, placaFicticia(prefijo, "1"), { nombre: `E2E reportes ${Date.now()}` });
    await sembrarExpress(page, placaFicticia(prefijo, "2"), { nombre: `E2E reportes ${Date.now()}` });

    const backoffice = registrarPeticiones(page, RUTA.backoffice);
    const catalogo = page.waitForResponse((r) => RUTA.catalogo.test(r.url()));
    const primera = await abrirReportes(page, RUTA.reporte);
    const cat = (await (await catalogo).json()) as Catalogo;
    expect(cat.reportes.map((r) => r.tipo)).toEqual(["vehiculos", "consultas", "saneamientos", "derivaciones"]);

    await test.step("pestañas = catálogo, en su orden; «Consumo por compañía» no existe para la compañía", async () => {
      await expect(pestanas(page).getByRole("tab")).toHaveText(cat.reportes.map((r) => r.nombre));
      await expect(pestanas(page).getByRole("tab", { name: "Consumo por compañía" })).toHaveCount(0);
      await expect(pestanas(page).getByRole("tab", { name: "Vehículos diagnosticados" })).toHaveAttribute("aria-selected", "true");
      await expect(selectorCompania(page)).toHaveCount(0);
    });

    await test.step("aviso de datos personales: visible, sin botón de cerrar, encima de las pestañas y de los botones", async () => {
      const aviso = page.getByText(/Este reporte contiene datos personales/);
      await expect(aviso).toBeVisible();
      await expect(page.getByText(/Ley 1581/).first()).toBeVisible();
      const contenedor = aviso.locator("xpath=ancestor-or-self::*[self::div or self::section][1]");
      await expect(contenedor.getByRole("button", { name: /cerrar|descartar/i })).toHaveCount(0);
      const [a, t] = [(await aviso.boundingBox())!, (await pestanas(page).boundingBox())!];
      expect(a.y + a.height).toBeLessThanOrEqual(t.y + 1);
      // FE-6 SP-18: junto a los botones se lee «Contiene datos personales» y los botones lo describen.
      await expect(page.getByText("Contiene datos personales", { exact: false }).first()).toBeVisible();
      await expect(excel(page)).toHaveAccessibleDescription(/datos personales/);
    });

    await test.step("tabla genérica: columnas = `columnas[].titulo` en orden; contador de registros", async () => {
      const p = (await (await primera).json()) as PaginaReporte;
      expect(p.ambito).toBe("COMPANIA");
      const tabla = page.getByRole("table").first();
      await expect(tabla.getByRole("columnheader")).toHaveText(p.columnas.map((c) => c.titulo));
      await expect(tabla.getByRole("columnheader", { name: "Compañía", exact: true })).toHaveCount(0);
      await expect(page.getByText(/^\d[\d.]* registros?$/).first()).toBeVisible();
      // La URL lleva el reporte y el periodo; nunca la placa (SP-18).
      await expect(page).toHaveURL(/[?&](reporte|tipo)=vehiculos/);
    });

    await test.step("filtro «Placa» por prefijo: va en la petición pero no en la URL; vuelve a la página 1", async () => {
      const pedido = page.waitForResponse((r) => RUTA.reporte.test(r.url()) && new URL(r.url()).searchParams.get("placa") === prefijo);
      await filtroPlaca(page).fill(prefijo);
      await filtroPlaca(page).press("Enter");
      const r = await pedido;
      expect(new URL(r.url()).searchParams.get("pagina") ?? "1").toBe("1");
      const p = (await r.json()) as PaginaReporte;
      expect(p.total).toBeGreaterThanOrEqual(2);
      expect(page.url()).not.toContain(prefijo);
      await revisarTemasOla(page, "reportes-vehiculos-1280", { captura: "R-1" });
    });

    await test.step("paginación: «Filas por página» cambia tamano y «Página siguiente» pide pagina=2", async () => {
      const filas = page.getByRole("combobox", { name: "Filas por página" });
      await expect(filas).toBeVisible();
      const opciones = await filas.locator("option").allTextContents();
      // El tamaño por defecto (25) ya es la opción menor: elegirla no cambia nada ni pide la API. Se elige otra opción.
      const actual = await filas.inputValue();
      const menor = opciones.map(Number).filter((n) => n > 0 && String(n) !== actual).sort((a, b) => a - b)[0];
      test.skip(!menor, "Sin otra opción en «Filas por página»");
      // Se reescribe la respuesta real para tener dos páginas: el total y, si la página real llega vacía (la semilla no
      // da para dos páginas), sus filas con las de la primera respuesta, para que la tabla y el paginador sigan pintados.
      let filasReales: PaginaReporte["filas"] = [];
      const dos = await reescribir<PaginaReporte>(page, RUTA.reporte, (real) => {
        if (real.filas.length) filasReales = real.filas;
        return { ...real, total: Math.max(real.total, menor * 2 + 1), filas: real.filas.length ? real.filas : filasReales };
      });
      const cambio = page.waitForResponse((r) => RUTA.reporte.test(r.url()) && new URL(r.url()).searchParams.get("tamano") === String(menor));
      await filas.selectOption(String(menor));
      await cambio;
      const siguiente = page.waitForResponse((r) => RUTA.reporte.test(r.url()) && new URL(r.url()).searchParams.get("pagina") === "2");
      await page.getByRole("button", { name: "Página siguiente" }).click();
      await siguiente;
      await expect(page.getByRole("button", { name: "Página 2" })).toHaveAttribute("aria-current", "page");
      await dos.quitar();
    });

    await test.step("pestaña «Consultas a fuentes»: cambia ?reporte= y pinta las columnas de ese tipo", async () => {
      const r = page.waitForResponse((x) => /\/reportes\/consultas\?/.test(x.url()));
      await pestanas(page).getByRole("tab", { name: "Consultas a fuentes" }).click();
      const p = (await (await r).json()) as PaginaReporte;
      await expect(page).toHaveURL(/[?&](reporte|tipo)=consultas/);
      await expect(page.getByRole("table").first().getByRole("columnheader")).toHaveText(p.columnas.map((c) => c.titulo));
      // contiene_pii=false: el aviso sigue, sin «(documentos y nombres)».
      await expect(page.getByText(/datos personales/).first()).toBeVisible();
      await expect(page.getByText(/documentos y nombres/)).toHaveCount(0);
    });

    expect(backoffice.map((u) => u.pathname), "la compañía nunca pide rutas del backoffice").toEqual([]);
    await ancho(page, 360);
    await sinDesbordeHorizontal(page, "reportes-360");
    await revisarTemasOla(page, "reportes-360", { captura: "R-1" });
    await ancho(page, 1280);
  });

  test("exportar Excel y PDF del filtro: nombre fijo del contrato, sin PII, y sin claves nuevas en el almacenamiento", async ({ page }) => {
    exigirCliente();
    test.skip(!prefijo, "Depende de la siembra del test anterior");
    test.setTimeout(240_000);
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, "/");
    const sesion = await leerSesion(page);
    test.skip(!sesion.capacidades.descargarReportes, "E2E_USUARIO no tiene reportes.descargar");
    await abrirReportes(page, RUTA.reporte);
    const antes = (await clavesAlmacenamiento(page)).filter((k) => !k.endsWith("flit-theme"));
    const filtrado = page.waitForResponse((r) => RUTA.reporte.test(r.url()) && new URL(r.url()).searchParams.get("placa") === prefijo);
    await filtroPlaca(page).fill(prefijo);
    await filtroPlaca(page).press("Enter");
    const p = (await (await filtrado).json()) as PaginaReporte;
    await expect(page.getByText(new RegExp(`Se exportan los ${p.total} registros del filtro`))).toBeVisible();

    const hoy = hoyBogota();
    const x = await exportar(page, excel(page), "xlsx", [prefijo]);
    expect(x.url.searchParams.get("placa")).toBe(prefijo);
    expect(x.url.searchParams.get("hasta")).toBe(hoy);
    expect(x.nombre).toMatch(new RegExp(`^diagnostico-reporte-vehiculos-\\d{8}-${hoy.replace(/-/g, "")}\\.xlsx$`));
    await exportar(page, pdf(page), "pdf", [prefijo]);

    const despues = (await clavesAlmacenamiento(page)).filter((k) => !k.endsWith("flit-theme"));
    expect(despues, "SP-18: ver y exportar no deja claves nuevas en localStorage ni sessionStorage").toEqual(antes);
  });

  test("límites: total > máximo deshabilita ese formato con motivo; 422 muestra filas y máximo; 429 pide esperar", async ({ page }) => {
    exigirCliente();
    test.setTimeout(180_000);
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, "/");
    const sesion = await leerSesion(page);
    test.skip(!sesion.capacidades.descargarReportes || !sesion.capacidades.verReportes, "Falta verReportes o descargarReportes");

    await test.step("total entre maximo_pdf y maximo_excel: PDF deshabilitado con «Acota el filtro», Excel habilitado", async () => {
      // Respuesta real reescrita dentro del contrato: vehiculos tiene maximo_pdf 1000 y maximo_excel 5000.
      const grande = await reescribir<PaginaReporte>(page, RUTA.reporte, (real) => ({ ...real, total: 1_500 }));
      await abrirReportes(page, RUTA.reporte);
      await expect(pdf(page)).toHaveAttribute("aria-disabled", "true");
      await expect(page.getByText(/Acota el filtro: máximo 1\.?000 registros en PDF/)).toBeVisible();
      await expect(excel(page)).not.toHaveAttribute("aria-disabled", "true");
      await revisarTemasOla(page, "reportes-limite-pdf-1280", { captura: "R-2", paginaCompleta: false });
      await grande.quitar();
    });

    await test.step("422 REPORTE_DEMASIADO_GRANDE: el mensaje dice filas y máximo; no hay descarga", async () => {
      await abrirReportes(page, RUTA.reporte);
      const ruta = /\/reportes\/vehiculos\/excel(\?|$)/;
      // Cuerpo completo del contrato §5 (con `filas` y `maximo`; fallarUnaVez solo lleva code/error).
      const grande = await responder(page, ruta, 422, { code: "REPORTE_DEMASIADO_GRANDE", error: "El reporte tiene más filas de las que admite el formato. Acota el rango o los filtros.", filas: 6120, maximo: 5000 });
      let descargo = false;
      page.once("download", () => (descargo = true));
      await excel(page).click();
      const alerta = page.getByRole("alert").filter({ hasText: /6\.?120/ });
      await expect(alerta).toBeVisible();
      await expect(alerta).toContainText(/5\.?000/);
      expect(descargo).toBe(false);
      await grande.quitar();
    });

    await test.step("429 EXPORTACION_EN_CURSO: pide esperar; el otro formato sigue activo", async () => {
      const ruta = /\/reportes\/vehiculos\/pdf(\?|$)/;
      const ocupado = await responder(page, ruta, 429, { code: "EXPORTACION_EN_CURSO", error: "Ya tienes una exportación en curso." }, { "Retry-After": "10" });
      await pdf(page).click();
      await expect(page.getByRole("alert").filter({ hasText: /espera|en curso|unos segundos/i })).toBeVisible();
      await expect(excel(page)).not.toHaveAttribute("aria-disabled", "true");
      await ocupado.quitar();
    });

    await test.step("en vuelo: «Generando…» con aria-busy mientras el servidor arma el archivo", async () => {
      const ruta = /\/reportes\/vehiculos\/excel(\?|$)/;
      // Solo se retiene la exportación real para ver el estado; luego se suelta y se descarga.
      const espera = await retener(page, ruta);
      await excel(page).click();
      await expect(page.getByRole("button", { name: "Generando…" })).toHaveAttribute("aria-busy", "true");
      await expect(page.getByText("Puede tardar unos segundos")).toBeVisible();
      const descarga = page.waitForEvent("download").catch(() => null);
      await espera.soltar();
      await descarga;
    });
  });

  test("sin descargarReportes no hay botones de exportar (variación de /sesion; la API sigue exigiendo el permiso)", async ({ page }) => {
    exigirCliente();
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, "/");
    await variarCapacidades(page, { descargarReportes: false });
    await abrirReportes(page, RUTA.reporte);
    await expect(page.getByRole("button", { name: /^Exportar (Excel|PDF)$/ })).toHaveCount(0);
  });

  test("cuatro estados: cargando, vacío del periodo, vacío por filtro y error con «Reintentar»", async ({ page }) => {
    exigirCliente();
    test.setTimeout(180_000);
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, "/");

    await test.step("cargando (respuesta real retenida): skeleton, «— registros» y exportar deshabilitado", async () => {
      const espera = await retener(page, RUTA.reporte);
      await page.goto(`${APP}/reportes`);
      await expect(H1(page)).toBeVisible();
      await expect(page.getByText(/— registros/)).toBeVisible();
      await expect(page.getByRole("button", { name: "Exportar Excel" })).toHaveAttribute("aria-disabled", "true");
      await revisarTemasOla(page, "reportes-cargando-1280", { captura: "R-3", axe: false });
      await espera.soltar();
    });

    await test.step("vacío del periodo (página del contrato con 0 filas)", async () => {
      const vacio = await reescribir<PaginaReporte>(page, RUTA.reporte, (real) => ({ ...real, total: 0, filas: [] }));
      await abrirReportes(page, RUTA.reporte);
      await expect(page.getByText("No hay registros en este periodo")).toBeVisible();
      await expect(page.getByRole("button", { name: "Exportar Excel" })).toHaveAttribute("aria-disabled", "true");
      await expect(page.getByRole("button", { name: "Exportar PDF" })).toHaveAttribute("aria-disabled", "true");
      await revisarTemasOla(page, "reportes-vacio-1280", { captura: "R-3" });
      await vacio.quitar();
    });

    await test.step("vacío por filtro: «Ningún registro coincide con los filtros» y «Limpiar filtros»", async () => {
      await abrirReportes(page, RUTA.reporte);
      await filtroPlaca(page).fill("ZZZ9");
      await filtroPlaca(page).press("Enter");
      await expect(page.getByText("Ningún registro coincide con los filtros")).toBeVisible();
      await revisarTemasOla(page, "reportes-vacio-filtros-1280", { captura: "R-3" });
      await page.getByRole("button", { name: "Limpiar filtros" }).first().click();
      await expect(filtroPlaca(page)).toHaveValue("");
    });

    await test.step("error 500: texto fijo con «Reintentar», sin el código técnico", async () => {
      const fallo = await fallarUnaVez(page, "GET", RUTA.reporte, { status: 500, code: "ERROR_INTERNO", error: "Fallo de prueba." }, { siempre: true });
      await page.goto(`${APP}/reportes`);
      await expect(page.getByRole("button", { name: "Reintentar" }).first()).toBeVisible();
      await expect(page.getByText("ERROR_INTERNO")).toHaveCount(0);
      await expect(page.getByText("Fallo de prueba.")).toHaveCount(0);
      await revisarTemasOla(page, "reportes-error-1280", { captura: "R-3" });
      await fallo.quitar();
      const ok = page.waitForResponse((r) => RUTA.reporte.test(r.url()));
      await page.getByRole("button", { name: "Reintentar" }).first().click();
      expect((await ok).status()).toBe(200);
    });
  });

  test("consumo es solo del backoffice: la API de compañía responde 404 REPORTE_NO_ENCONTRADO", async ({ page }) => {
    exigirCliente();
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, "/");
    const hoy = hoyBogota();
    const r = await page.request.get(`${API}/reportes/consumo?desde=${sumarDias(hoy, -29)}&hasta=${hoy}`);
    expect(r.status()).toBe(404);
    expect((await r.json()).code).toBe("REPORTE_NO_ENCONTRADO");
    // Y la UI no ofrece la pestaña aunque se fuerce por la URL.
    await page.goto(`${APP}/reportes?reporte=consumo`);
    await expect(H1(page)).toBeVisible();
    await expect(pestanas(page).getByRole("tab", { name: "Consumo por compañía" })).toHaveCount(0);
  });
});

test.describe("FE-6 reportes del super admin", () => {
  test("«Todas las compañías» usa /global, antepone «Compañía» y ofrece «Consumo por compañía»; exporta con -global", async ({ page }) => {
    exigirBackoffice();
    test.setTimeout(240_000);
    await entrar(page, BACKOFFICE.usuario!, BACKOFFICE.clave!, "/");
    const sesion = await leerSesion(page);
    test.skip(!sesion.capacidades.backoffice, "El usuario no tiene la capacidad backoffice");

    const catalogo = page.waitForResponse((r) => RUTA.catalogoBackoffice.test(r.url()));
    const primera = await abrirReportes(page, RUTA.reporteGlobal);
    const cat = (await (await catalogo).json()) as Catalogo;
    expect(cat.reportes.map((r) => r.tipo)).toEqual(["vehiculos", "consultas", "saneamientos", "derivaciones", "consumo"]);
    await expect(pestanas(page).getByRole("tab")).toHaveText(cat.reportes.map((r) => r.nombre));
    await expect(selectorCompania(page)).toHaveValue("Todas las compañías");

    const p = (await (await primera).json()) as PaginaReporte;
    expect(p.ambito).toBe("BACKOFFICE_GLOBAL");
    expect(p.columnas[0].clave).toBe("empresa_nombre");
    await expect(page.getByRole("table").first().getByRole("columnheader").first()).toHaveText("Compañía");
    await revisarTemasOla(page, "reportes-global-1280", { captura: "R-4" });

    await test.step("Consumo por compañía: aviso informativo sin datos personales; exporta Excel con sufijo -global", async () => {
      const r = page.waitForResponse((x) => /\/backoffice\/reportes\/consumo\/global\?/.test(x.url()));
      await pestanas(page).getByRole("tab", { name: "Consumo por compañía" }).click();
      expect((await r).status()).toBe(200);
      await expect(page.getByText("Cada descarga queda registrada con tu usuario.")).toBeVisible();
      await expect(page.getByText(/Este reporte contiene datos personales/)).toHaveCount(0);
      await revisarTemasOla(page, "reportes-consumo-1280", { captura: "R-4" });
      if (sesion.capacidades.descargarReportes && (await excel(page).getAttribute("aria-disabled")) !== "true") {
        const { nombre } = await exportar(page, excel(page), "xlsx", []);
        expect(nombre).toMatch(/^diagnostico-reporte-consumo-\d{8}-\d{8}-global\.xlsx$/);
      }
    });
  });

  test("al elegir una compañía usa /backoffice/reportes/{tipo}?empresa= y la columna «Compañía» desaparece", async ({ page }) => {
    exigirBackoffice();
    test.setTimeout(180_000);
    await entrar(page, BACKOFFICE.usuario!, BACKOFFICE.clave!, "/");
    const empresas = (await (await page.request.get(`${API}/backoffice/empresas`)).json()) as { id: string; nombre: string }[];
    const empresa = empresas.find((e) => e.id === EMPRESA) ?? empresas[0];
    test.skip(!empresa, "No hay compañías con Diagnóstico activo");

    await abrirReportes(page, RUTA.reporteGlobal);
    const r = page.waitForResponse((x) => RUTA.reporteEmpresa.test(x.url()) && new URL(x.url()).searchParams.get("empresa") === empresa.id);
    await selectorCompania(page).fill(empresa.nombre.slice(0, 6));
    await page.getByRole("listbox", { name: "Compañía" }).getByRole("option", { name: new RegExp(empresa.nombre.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")) }).first().click();
    const p = (await (await r).json()) as PaginaReporte;
    expect(p.ambito).toBe("BACKOFFICE_EMPRESA");
    expect(p.columnas.map((c) => c.clave)).not.toContain("empresa_nombre");
    await expect(page.getByRole("table").first().getByRole("columnheader", { name: "Compañía", exact: true })).toHaveCount(0);
    await revisarTemasOla(page, "reportes-backoffice-compania-1280", { captura: "R-5" });
  });
});
