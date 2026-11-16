import { expect, test, type Locator, type Page } from "@playwright/test";
import {
  APP,
  BACKOFFICE,
  CLIENTE,
  EMPRESA,
  ancho,
  entrar,
  esperarTerminados,
  exigirBackoffice,
  exigirCliente,
  placaFicticia,
  prefijoFicticio,
  sembrarMasivo,
  sinDesbordeHorizontal,
} from "./saneamiento-comun";
import { abrirAcciones, celdaDictamen, disparadorAcciones, itemAuditoria, itemDictamen, leerSesion, nombresItems, variarCapacidades } from "./ola-ux-comun";
import {
  BASURA_DOCUMENTOS,
  FUENTES_NOMBRE,
  TEXTO_CAMBIO_PROPIETARIO,
  capturar,
  esperarTooltip,
  estadoTooltip,
  pistasDePii,
  revisarTemasReq,
  variarGrilla,
  type VariacionFila,
} from "./ola-requerimiento-comun";

/**
 * HU #14584 · Detalle del lote: tooltip de celda, métricas en una fila, filtros nuevos, menú «Acciones» con flip y
 * Dictamen/Auditoría en el menú (cliente y backoffice). spec-ui §4, §5, §6 (A4.1–A4.3, A5.1–A5.4, A6.1–A6.9); SP-19/SP-21.
 *
 * Modo de la API: suite local real con simulación. Siembra un Masivo de 3 placas ficticias del prefijo de la corrida:
 * terminada en 1 (éxito), en 2 (SIMIT en rojo) y en 7 (RUNT agotado: no termina). `page.route` solo para variar la grilla
 * REAL dentro del contrato 1.6.0:
 *   - `veredicto` (V1 VIABLE, V2 BLOQUEADO, V7 null) y `cambio_propietario` (V1 true, V2 false, V7 null);
 *   - AVALUO fuera de `columnas` para tener una celda «Sin solicitar» determinista;
 *   - SP-19: los campos de documento del contrato 1.5.0 (retirados) con un documento ficticio, que el front no debe pintar;
 *   - capacidades REALES de /sesion (`verAuditoria`) para la visibilidad del ítem de auditoría (la API sigue exigiéndola).
 * Variables: E2E_USUARIO / E2E_CLAVE (compañía) y, para el backoffice, E2E_BACKOFFICE_USUARIO / E2E_BACKOFFICE_CLAVE /
 * E2E_EMPRESA (SuperAdmin FLIT y el uuid de la compañía de E2E_USUARIO).
 *
 * Lista congelada (componentes de la Fase 2; ajustar en la Fase 3 si el nombre final cambia):
 *   - table «/semáforo de consultas/i» · rowheader «<placa>» · td[data-fuente=<FUENTE>] · td[data-columna=dictamen]
 *   - disparador del tooltip: el enlace «Ver detalle de <fuente> de <placa>: …» o `span[tabindex=0]` «<fuente> de <placa>:
 *     Sin solicitar»; burbuja role=tooltip referida por aria-describedby (contenido montado al abrir o con opacidad 0)
 *   - [aria-label="Filtros del lote"] (embudo) · button «<Métrica>: <n>» (aria-pressed)
 *   - combobox «Fuente» · combobox «Estado» (deshabilitado hasta elegir fuente) · combobox «Dictamen» ·
 *     button «/^Cambio de propietario \(\d+\)$/» (aria-pressed) · button «Limpiar filtros» · textbox «Buscar por placa o VIN» ·
 *     texto «Mostrando N de M vehículos» · texto «Ningún vehículo coincide»
 *   - button «Acciones de la placa <placa>» · menu (data-lado=abajo|arriba en el panel o un ancestro) · separator ·
 *     menuitem «Ver dictamen de <placa>» · «Auditoría del dictamen de <placa>»
 *   - button «Cambio de propietario» (toggletip 24×24 junto a la placa)
 *   - columnheader «Acciones» en el backoffice (nuevo)
 */
test.describe.configure({ mode: "serial" });

const SPEC = "grilla";

const grilla = (page: Page) => page.getByRole("table", { name: /semáforo de consultas/i });
const fila = (page: Page, placa: string) => grilla(page).getByRole("row").filter({ has: page.getByRole("rowheader", { name: new RegExp(placa) }) });
const celda = (page: Page, placa: string, fuente: string) => fila(page, placa).locator(`td[data-fuente="${fuente}"]`);
/** Disparador del tooltip de la celda: el enlace del chip o el span enfocable que lo envuelve. */
const disparadorCelda = (page: Page, placa: string, fuente: string) => celda(page, placa, fuente).locator('a[href], [tabindex="0"]').first();
const contadorFilas = (page: Page) => page.getByText(/^Mostrando \d+ de \d+ vehículos$/);
const embudo = (page: Page) => page.locator('[aria-label="Filtros del lote"]');
const METRICA = /^(Vehículos|Terminados|Con error|Con deuda|Bloqueados|Listos para trámite): \d+$/;

let lote = "";
let V1 = "";
let V2 = "";
let V7 = "";

function variaciones(conBasura = false): Record<string, VariacionFila> {
  const b = conBasura ? { basura: BASURA_DOCUMENTOS } : {};
  return {
    [V1]: { veredicto: "VIABLE", cambio_propietario: true, ...b },
    [V2]: { veredicto: "BLOQUEADO", cambio_propietario: false, ...b },
    [V7]: { veredicto: null, cambio_propietario: null, ...b },
  };
}

async function abrirLote(page: Page, opciones: { basura?: boolean; sinPedir?: string[] } = {}) {
  await variarGrilla(page, { porPlaca: variaciones(opciones.basura), sinPedir: opciones.sinPedir ?? ["AVALUO"] });
  await page.goto(`${APP}/diagnosticos/${lote}`);
  await expect(grilla(page)).toBeVisible();
  await expect(fila(page, V7)).toBeVisible();
}

async function filasVisibles(page: Page): Promise<string[]> {
  return grilla(page)
    .getByRole("rowheader")
    .evaluateAll((ths) => ths.filter((t) => (t as HTMLElement).offsetParent !== null).map((t) => (t.textContent ?? "").replace(/\s+/g, " ").trim()));
}
const visibles = async (page: Page) => (await filasVisibles(page)).map((t) => [V1, V2, V7].find((p) => t.includes(p))).filter(Boolean);

test.describe("HU #14584 grilla del detalle", () => {
  test("siembra: Masivo de 3 placas; sin columnas de documento, cabecera RUNT/SIMIT intacta y texto al pie", async ({ page }) => {
    exigirCliente();
    test.setTimeout(420_000);
    const prefijo = prefijoFicticio();
    [V1, V2, V7] = [placaFicticia(prefijo, "1"), placaFicticia(prefijo, "2"), placaFicticia(prefijo, "7")];
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, "/diagnosticos");
    ({ diagnosticoId: lote } = await sembrarMasivo(page, [V1, V2, V7], { nombre: `E2E grilla detalle ${Date.now()}` }));
    await esperarTerminados(page, lote, [V1, V2]);
    await abrirLote(page);

    // A6.1 / SP-4: sin columnas de documento.
    for (const nombre of ["Documento declarado", "Documento del propietario"]) await expect(grilla(page).getByRole("columnheader", { name: nombre })).toHaveCount(0);
    await expect(grilla(page).locator('td[data-columna^="documento-"]')).toHaveCount(0);
    // A6.2: cabecera de dos niveles intacta.
    await expect(grilla(page).locator('th[scope="colgroup"]')).toHaveCount(2);
    // A6.9: texto al pie.
    await expect(page.getByText(/Dictamen y auditoría: en «Acciones»/)).toBeVisible();
    await expect(page.getByText(/Pasa el puntero o enfoca un estado para ver la fecha del dato/)).toBeVisible();
    await capturar(page, SPEC, 1, "sin-columnas-documento-y-pie", { paginaCompleta: true });
  });

  test("tooltip de celda: hover tras ~300 ms, foco al instante, Esc cierra sin mover el foco; «No se pidió en este lote» solo en el tooltip", async ({ page }) => {
    exigirCliente();
    test.skip(!lote, "Depende de la siembra");
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, `/diagnosticos/${lote}`);
    await abrirLote(page);

    // A6.3: a ≥ 900 px la celda solo pinta el chip; el motivo no se ve fuera del tooltip.
    const motivoVisible = () =>
      page.getByText("No se pidió en este lote", { exact: true }).evaluateAll((ns) =>
        ns.filter((n) => {
          const r = (n as HTMLElement).getBoundingClientRect();
          return r.width > 0 && r.height > 0 && Number(getComputedStyle(n as HTMLElement).opacity) > 0.5 && !(n as HTMLElement).closest('[role="tooltip"]');
        }).length,
      );
    expect(await motivoVisible(), "motivo fuera del tooltip a 1280 px").toBe(0);
    await expect(celda(page, V1, "RUNT").getByText(/^Dato del /)).toBeHidden();

    // Sin solicitar (AVALUO fuera de columnas): disparador enfocable con nombre «<fuente> de <placa>: Sin solicitar».
    const sinSolicitar = disparadorCelda(page, V1, "AVALUO");
    await expect(sinSolicitar).toHaveAttribute("aria-label", new RegExp(`${FUENTES_NOMBRE.AVALUO} de ${V1}: Sin solicitar`));
    await sinSolicitar.hover();
    await page.waitForTimeout(120);
    expect((await estadoTooltip(sinSolicitar)).abierto, "hover: aún cerrado antes de 300 ms").toBe(false);
    await esperarTooltip(sinSolicitar, true, 1_500);
    const t = await estadoTooltip(sinSolicitar);
    expect(t.texto).toContain("Sin solicitar");
    expect(t.texto).toContain("No se pidió en este lote");
    await capturar(page, SPEC, 2, "tooltip-hover-sin-solicitar");
    // A5.4: el puntero puede pasar a la burbuja sin que se cierre.
    const burbuja = page.locator(`[id="${t.id}"]`);
    await burbuja.hover();
    await page.waitForTimeout(250);
    expect((await estadoTooltip(sinSolicitar)).abierto, "hoverable (1.4.13)").toBe(true);
    await page.mouse.move(2, 2);
    await esperarTooltip(sinSolicitar, false);

    // Foco por teclado: abre al instante; Esc cierra y el foco sigue en el disparador; A5.2 aria-describedby al tooltip.
    const runt = disparadorCelda(page, V1, "RUNT");
    await page.keyboard.press("Shift");
    await runt.focus();
    await esperarTooltip(runt, true, 400);
    const r = await estadoTooltip(runt);
    expect(r.texto).toMatch(/Ejecutado/);
    expect(r.texto).toMatch(/Dato del \d{2}\/\d{2}\/\d{4} \d{2}:\d{2}|Consultado en este lote/);
    await capturar(page, SPEC, 3, "tooltip-foco-dato-del");
    await page.keyboard.press("Escape");
    await esperarTooltip(runt, false);
    await expect(runt).toBeFocused();
    await capturar(page, SPEC, 4, "tooltip-esc-cerrado-foco");
    await revisarTemasReq(page, "grilla-tooltip-1280", { captura: "GD-1", paginaCompleta: false });

    // < 900 px: la línea secundaria se ve en la tarjeta (además del tooltip).
    await ancho(page, 360);
    await expect(celda(page, V1, "AVALUO").getByText("No se pidió en este lote", { exact: true })).toBeVisible();
    await capturar(page, SPEC, 5, "tarjeta-360-motivo-en-linea", { paginaCompleta: true });
    await sinDesbordeHorizontal(page, "grilla-360");
    await ancho(page, 1280);
  });

  test("métricas del lote en una sola fila (1280 y 1024) y carril deslizable a 360 px", async ({ page }) => {
    exigirCliente();
    test.skip(!lote, "Depende de la siembra");
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, `/diagnosticos/${lote}`);
    await abrirLote(page);
    for (const px of [1280, 1024] as const) {
      await page.setViewportSize({ width: px, height: 800 });
      const botones = embudo(page).getByRole("button", { name: METRICA });
      const n = await botones.count();
      expect(n, "6 métricas").toBe(6);
      const tops = await botones.evaluateAll((bs) => bs.map((b) => Math.round(b.getBoundingClientRect().top)));
      expect(Math.max(...tops) - Math.min(...tops), `${px}: las métricas en una fila`).toBeLessThanOrEqual(2);
      // A6.5: iconos decorativos.
      const iconosConNombre = await embudo(page).locator("img").evaluateAll((is) => is.filter((i) => (i.getAttribute("alt") ?? "") !== "").length);
      expect(iconosConNombre, "iconos del embudo con alt vacío").toBe(0);
      await capturar(page, SPEC, px === 1280 ? 6 : 7, `metricas-una-fila-${px}`);
    }
    await revisarTemasReq(page, "metricas-1280", { captura: "GD-2", paginaCompleta: false });
    await ancho(page, 360);
    const desliza = await embudo(page).evaluate((el) => el.scrollWidth > el.clientWidth || /auto|scroll/.test(getComputedStyle(el).overflowX));
    expect(desliza, "360: carril deslizable").toBe(true);
    await capturar(page, SPEC, 8, "metricas-carril-360");
    await sinDesbordeHorizontal(page, "metricas-360");
    await ancho(page, 1280);
  });

  test("filtros: fuente + estado, cambio de propietario, dictamen, combinados (AND), «Limpiar filtros» y vacío por filtro", async ({ page }) => {
    exigirCliente();
    test.skip(!lote, "Depende de la siembra");
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, `/diagnosticos/${lote}`);
    await abrirLote(page);
    const fuente = page.getByRole("combobox", { name: "Fuente", exact: true });
    const estado = page.getByRole("combobox", { name: "Estado", exact: true });
    const dictamen = page.getByRole("combobox", { name: "Dictamen", exact: true });
    const limpiar = page.getByRole("button", { name: "Limpiar filtros" }).first();
    const buscador = page.getByRole("textbox", { name: "Buscar por placa o VIN" });
    await expect(contadorFilas(page)).toHaveText("Mostrando 3 de 3 vehículos");
    await expect(limpiar).toBeDisabled();

    // Fuente: solo las pedidas (sin AVALUO, que se sacó de columnas) + «Todas».
    const opcionesFuente = await fuente.locator("option").allTextContents();
    expect(opcionesFuente).toContain("Todas");
    expect(opcionesFuente).toContain(FUENTES_NOMBRE.RUNT);
    expect(opcionesFuente).not.toContain(FUENTES_NOMBRE.AVALUO);
    // Estado deshabilitado hasta elegir fuente, con el motivo descrito.
    await expect(estado).toBeDisabled();
    await expect(estado).toHaveAccessibleDescription(/Elige primero una fuente/);

    // Fuente + estado (p. ej. «SIMIT vehículo» = el primer estado presente, con su conteo).
    await fuente.selectOption({ label: FUENTES_NOMBRE.SIMIT_VEHICULO });
    await expect(estado).toBeEnabled();
    const opcionesEstado = (await estado.locator("option").allTextContents()).filter((o) => /\(\d+\)$/.test(o));
    expect(opcionesEstado.length, "estados con conteo").toBeGreaterThan(0);
    const elegido = opcionesEstado[0];
    const [, rotulo, cuenta] = elegido.match(/^(.*) \((\d+)\)$/)!;
    await estado.selectOption({ label: elegido });
    await expect(contadorFilas(page)).toHaveText(`Mostrando ${cuenta} de 3 vehículos`);
    for (const p of await visibles(page)) await expect(celda(page, p!, "SIMIT_VEHICULO")).toContainText(rotulo);
    await capturar(page, SPEC, 9, "filtro-fuente-estado");
    await expect(limpiar).toBeEnabled();

    // «Limpiar filtros» restablece todo y lleva el foco al buscador.
    await limpiar.click();
    await expect(contadorFilas(page)).toHaveText("Mostrando 3 de 3 vehículos");
    await expect(fuente).toHaveValue(/^(|TODAS|todas)$/);
    await expect(buscador).toBeFocused();

    // Cambio de propietario: solo con algún valor no nulo; cuenta las `true`.
    const cambio = page.getByRole("button", { name: /^Cambio de propietario \(\d+\)$/ });
    await expect(cambio).toHaveAccessibleName("Cambio de propietario (1)");
    await expect(cambio).toHaveAttribute("aria-pressed", "false");
    await cambio.click();
    await expect(cambio).toHaveAttribute("aria-pressed", "true");
    expect(await visibles(page)).toEqual([V1]);
    await capturar(page, SPEC, 10, "filtro-cambio-propietario");
    await limpiar.click();

    // Dictamen: Viable → V1; Bloqueado → V2.
    await dictamen.selectOption({ label: "Viable" });
    expect(await visibles(page)).toEqual([V1]);
    await dictamen.selectOption({ label: "Bloqueado" });
    expect(await visibles(page)).toEqual([V2]);
    await capturar(page, SPEC, 11, "filtro-dictamen-bloqueado");

    // AND: dictamen Bloqueado + cambio de propietario → vacío por filtro con «Limpiar filtros».
    await cambio.click();
    await expect(page.getByText("Ningún vehículo coincide", { exact: true })).toBeVisible();
    await capturar(page, SPEC, 12, "filtro-vacio-limpiar");
    await revisarTemasReq(page, "filtros-vacio-1280", { captura: "GD-3", paginaCompleta: false });

    // La métrica activa también se apaga al limpiar.
    await page.getByRole("button", { name: "Limpiar filtros" }).last().click();
    await embudo(page).getByRole("button", { name: /^Terminados: \d+$/ }).click();
    await expect(embudo(page).getByRole("button", { name: /^Terminados: \d+$/ })).toHaveAttribute("aria-pressed", "true");
    await limpiar.click();
    await expect(embudo(page).getByRole("button", { name: /^Terminados: \d+$/ })).toHaveAttribute("aria-pressed", "false");
    await capturar(page, SPEC, 13, "limpiar-filtros-apaga-metrica");
    await expect(contadorFilas(page)).toHaveText("Mostrando 3 de 3 vehículos");

    // SP-21: lo que queda en la URL o en el almacenamiento son códigos (sin documentos ni correos).
    await fuente.selectOption({ label: FUENTES_NOMBRE.SIMIT_VEHICULO });
    await dictamen.selectOption({ label: "Viable" });
    const guardado = await page.evaluate(() => [location.search, ...Object.values(localStorage), ...Object.values(sessionStorage)].join(" "));
    expect(guardado).not.toMatch(/@|\*{4}\d{4}|\d{7,}/);
    await revisarTemasReq(page, "filtros-1280", { captura: "GD-4", paginaCompleta: false });
    await ancho(page, 360);
    await sinDesbordeHorizontal(page, "filtros-360");
    await ancho(page, 1280);
  });

  test("menú «Acciones»: abre hacia arriba en la última fila pegada al borde inferior y queda entero en el viewport", async ({ page }) => {
    exigirCliente();
    test.skip(!lote, "Depende de la siembra");
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, `/diagnosticos/${lote}`);
    await abrirLote(page);

    // Normal (no regresión, A4.2): con espacio abajo, abre abajo.
    await page.setViewportSize({ width: 1280, height: 1000 });
    await disparadorAcciones(page, V1).scrollIntoViewIfNeeded();
    const { menu: m1 } = await abrirAcciones(page, V1);
    const lado = (m: Locator) => m.evaluate((n) => n.getAttribute("data-lado") ?? n.closest("[data-lado]")?.getAttribute("data-lado") ?? null);
    const d1 = (await disparadorAcciones(page, V1).boundingBox())!;
    const c1 = (await m1.boundingBox())!;
    if (1000 - (d1.y + d1.height) - 8 >= c1.height) expect(await lado(m1)).toBe("abajo");
    await page.keyboard.press("Escape");

    // A4.1: disparador de la última fila a ~40 px del borde inferior → arriba y sin scroll interno.
    const ultima = disparadorAcciones(page, V7);
    await ultima.evaluate((el) => el.scrollIntoView({ block: "end" }));
    let d = (await ultima.boundingBox())!;
    await page.setViewportSize({ width: 1280, height: Math.max(420, Math.round(d.y + d.height + 40)) });
    await ultima.evaluate((el) => el.scrollIntoView({ block: "end" }));
    await page.waitForTimeout(200);
    // El dock (z-40) puede tapar el disparador: se abre con teclado.
    await ultima.focus();
    await page.keyboard.press("Enter");
    const menu = page.getByRole("menu");
    await expect(menu).toBeVisible();
    d = (await ultima.boundingBox())!;
    const c = (await menu.boundingBox())!;
    const vista = page.viewportSize()!;
    if (vista.height - (d.y + d.height) - 8 < c.height) {
      expect(await lado(menu), "sin espacio abajo: abre arriba").toBe("arriba");
      expect(c.y + c.height, "el panel termina sobre el disparador").toBeLessThanOrEqual(d.y + 1);
    } else {
      test.info().annotations.push({ type: "precondición", description: "El disparador quedó con espacio abajo: no se pudo forzar el flip" });
    }
    expect(c.y).toBeGreaterThanOrEqual(0);
    expect(c.y + c.height).toBeLessThanOrEqual(vista.height + 1);
    const items = menu.getByRole("menuitem");
    for (let i = 0; i < (await items.count()); i++) await expect(items.nth(i)).toBeInViewport();
    expect(await menu.evaluate((n) => n.scrollHeight <= n.clientHeight + 1), "todos los ítems sin scroll interno").toBe(true);
    await capturar(page, SPEC, 14, "menu-acciones-abre-arriba-ultima-fila");
    await revisarTemasReq(page, "menu-flip", { captura: "GD-5", paginaCompleta: false });
    await page.keyboard.press("Escape");
    await expect(ultima).toBeFocused();
    await page.setViewportSize({ width: 1280, height: 800 });
  });

  test("cliente: Dictamen y Auditoría en «Acciones» (con y sin verAuditoria), fuera de la columna Placa; abrir y volver el foco", async ({ page }) => {
    exigirCliente();
    test.skip(!lote, "Depende de la siembra");
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, `/diagnosticos/${lote}`);

    for (const verAuditoria of [false, true]) {
      // Variación de /sesion dentro del contrato: solo lo que pinta el front.
      const v = await variarCapacidades(page, { verAuditoria });
      await abrirLote(page);
      // A6.6: la celda de placa no tiene botones de dictamen ni de auditoría; la columna Dictamen es dato.
      const placa = fila(page, V1).getByRole("rowheader");
      await expect(placa.getByRole("button", { name: /dictamen|auditor/i })).toHaveCount(0);
      await expect(celdaDictamen(page, V1)).toHaveText(/Viable/);
      await expect(celdaDictamen(page, V1).getByRole("button")).toHaveCount(0);

      const { menu } = await abrirAcciones(page, V1);
      await expect(itemDictamen(page, V1)).toBeVisible();
      await expect(itemAuditoria(page, V1)).toHaveCount(verAuditoria ? 1 : 0);
      const nombres = await nombresItems(menu);
      expect(nombres[0], "el grupo Dictamen va primero").toMatch(/^Ver dictamen/);
      await expect(menu.getByRole("separator")).toHaveCount(nombres.length > (verAuditoria ? 2 : 1) ? 1 : 0);
      await capturar(page, SPEC, verAuditoria ? 16 : 15, `menu-cliente-${verAuditoria ? "con" : "sin"}-auditoria`);
      await page.keyboard.press("Escape");
      await v.quitar();
    }

    // Abrir el dictamen desde el menú; Esc devuelve el foco a «Acciones».
    await abrirLote(page);
    const { disparador } = await abrirAcciones(page, V1);
    await itemDictamen(page, V1).click();
    const dialogo = page.getByRole("dialog", { name: `Dictamen · ${V1}` });
    await expect(dialogo).toBeVisible();
    await capturar(page, SPEC, 17, "dictamen-desde-acciones");
    await page.keyboard.press("Escape");
    await expect(dialogo).toHaveCount(0);
    await expect(disparador).toBeFocused();
    await revisarTemasReq(page, "acciones-dictamen-1280", { captura: "GD-6", paginaCompleta: false });
  });

  test("icono de cambio de propietario solo con true: 24×24, junto a la placa, tooltip con el literal fijo", async ({ page }) => {
    exigirCliente();
    test.skip(!lote, "Depende de la siembra");
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, `/diagnosticos/${lote}`);
    await abrirLote(page);
    const icono = (p: string) => fila(page, p).getByRole("rowheader").getByRole("button", { name: "Cambio de propietario", exact: true });
    await expect(icono(V1)).toHaveCount(1);
    await expect(icono(V2)).toHaveCount(0);
    await expect(icono(V7)).toHaveCount(0);
    const caja = (await icono(V1).boundingBox())!;
    expect(caja.width).toBeGreaterThanOrEqual(24);
    expect(caja.height).toBeGreaterThanOrEqual(24);
    await page.keyboard.press("Shift");
    await icono(V1).focus();
    await esperarTooltip(icono(V1), true, 400);
    expect((await estadoTooltip(icono(V1))).texto).toMatch(TEXTO_CAMBIO_PROPIETARIO);
    await capturar(page, SPEC, 18, "icono-cambio-propietario-tooltip");
    await page.keyboard.press("Escape");
    await esperarTooltip(icono(V1), false);
  });

  test("SP-19: con campos de documento en la respuesta, nada del documento llega al DOM (texto, title, aria-*, data-*, tooltips)", async ({ page }) => {
    exigirCliente();
    test.skip(!lote, "Depende de la siembra");
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, `/diagnosticos/${lote}`);
    await abrirLote(page, { basura: true });
    expect(await pistasDePii(page), "DOM del detalle").toEqual([]);
    // Con el tooltip del icono y el menú abiertos (contenido montado al abrir).
    await fila(page, V1).getByRole("rowheader").getByRole("button", { name: "Cambio de propietario", exact: true }).hover();
    await page.waitForTimeout(500);
    await abrirAcciones(page, V1);
    expect(await pistasDePii(page), "DOM con tooltip y menú abiertos").toEqual([]);
    await capturar(page, SPEC, 19, "sp19-sin-documento-en-dom");
  });

  test("backoffice: columna «Acciones» con Dictamen y Auditoría según capacidad; nada bajo la placa; misma barra de filtros", async ({ page }) => {
    exigirBackoffice();
    test.skip(!lote, "Depende de la siembra (lote de la compañía E2E_EMPRESA)");
    await page.setViewportSize({ width: 1280, height: 800 });
    await entrar(page, BACKOFFICE.usuario!, BACKOFFICE.clave!, "/");
    const sesion = await leerSesion(page);
    await variarGrilla(page, { porPlaca: variaciones(true), backoffice: true });
    await page.goto(`${APP}/backoffice/diagnosticos/${lote}?empresa=${encodeURIComponent(EMPRESA!)}`);
    await expect(grilla(page)).toBeVisible();

    await expect(grilla(page).getByRole("columnheader", { name: "Acciones", exact: true })).toBeVisible();
    for (const nombre of ["Documento declarado", "Documento del propietario"]) await expect(grilla(page).getByRole("columnheader", { name: nombre })).toHaveCount(0);
    const placa = fila(page, V1).getByRole("rowheader");
    await expect(placa.getByRole("button", { name: /dictamen|auditor/i })).toHaveCount(0);
    await expect(placa.getByRole("button", { name: "Cambio de propietario", exact: true })).toHaveCount(1);

    await abrirAcciones(page, V1);
    await expect(itemDictamen(page, V1)).toBeVisible();
    await expect(itemAuditoria(page, V1)).toHaveCount(sesion.capacidades.verAuditoria ? 1 : 0);
    await capturar(page, SPEC, 20, "backoffice-menu-dictamen-auditoria");
    await page.keyboard.press("Escape");

    // Decisión del orquestador: la barra de filtros nueva también en el backoffice.
    await expect(page.getByRole("combobox", { name: "Fuente", exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: /^Cambio de propietario \(\d+\)$/ })).toBeVisible();
    expect(await pistasDePii(page), "SP-19 en el backoffice").toEqual([]);
    await capturar(page, SPEC, 21, "backoffice-barra-filtros", { paginaCompleta: true });
    await revisarTemasReq(page, "backoffice-grilla-1280", { captura: "GD-7" });
  });
});
