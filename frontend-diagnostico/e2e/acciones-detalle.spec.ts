import { expect, test, type Locator, type Page } from "@playwright/test";
import {
  APP,
  CLIENTE,
  ancho,
  entrar,
  esperarTerminados,
  exigirCliente,
  placaFicticia,
  prefijoFicticio,
  sembrarMasivo,
  sinDesbordeHorizontal,
} from "./saneamiento-comun";
import {
  ITEM,
  ORDEN_ITEMS,
  abrirAcciones,
  disparadorAcciones,
  esperarDeshabilitadoConMotivo,
  leerSesion,
  nombresItems,
  revisarTemasOla,
  variarCapacidades,
  type Sesion,
} from "./ola-ux-comun";

/**
 * FE-2 · Detalle express/masivo: menú «Acciones» por fila y encabezados RUNT/SIMIT agrupados (spec-ui §2; V2 y V3 adoptados).
 *
 * Gherkin (particion-hus.md FE-2): «desplegable de acciones» y «encabezados agrupados».
 * Orden de ítems: el de spec-ui §2.3 con V2 (Sanear/Ver saneamiento · Crear/Ver trámite · Ver deuda · PDF · Reintentar). El
 * Gherkin de FE-2 lista «Ver deuda» primero; manda V2 (decisiones-usuario.md).
 *
 * Modo de la API: suite local real con simulación. Siembra un Masivo con tres placas ficticias del prefijo de la corrida:
 * terminada en 1 (éxito), en 2 (SIMIT en rojo) y en 7 (RUNT agotado: nunca termina → PDF deshabilitado con motivo).
 * `page.route` solo en el último test: varía las capacidades REALES de /sesion para la fila sin acciones (la API sigue
 * exigiendo cada permiso).
 * Variables: E2E_USUARIO / E2E_CLAVE (compañía, sin rol admin: sin reprocesarConsultas).
 *
 * Lista congelada (componentes nuevos de la Fase 2; ajustar en la Fase 3 si el nombre final cambia):
 *   - button «Acciones de la placa <placa>» (aria-haspopup=menu, aria-expanded) · menu · menuitem «Ver dictamen de <placa>» ·
 *     «Auditoría del dictamen de <placa>» (oleada 2026-11-16, grupo Dictamen + separator) · menuitem «Solicitar saneamiento» ·
 *     «Quitar solicitud de saneamiento» · «Ver saneamiento» · «Crear trámite» · «Ver trámite #N» · «Ver deuda» ·
 *     «Descargar PDF» · «Reintentar consultas»
 *   - texto sr-only «Sin acciones para la placa <placa>» (el chip «Saneamiento: …» de la fila no se usa: el carril fe1 lo
 *     retira; el estado se lee del nombre del menuitem)
 *   - columnheader «Acciones» · th[scope=colgroup] «RUNT» / «SIMIT» · columnheader «RUNT vehículo», «SIMIT propietario»…
 *   - table «/semáforo de consultas/i» · rowheader «<placa>»
 */
test.describe.configure({ mode: "serial" });

const grilla = (page: Page) => page.getByRole("table", { name: /semáforo de consultas/i });

/** Texto visible de un elemento, sin los nodos `sr-only`. */
async function textoVisible(el: Locator): Promise<string> {
  return el.evaluate((n) => {
    const copia = n.cloneNode(true) as HTMLElement;
    copia.querySelectorAll(".sr-only").forEach((s) => s.remove());
    return (copia.textContent ?? "").replace(/\s+/g, " ").trim();
  });
}

test.describe("FE-2 menú de acciones y cabecera RUNT/SIMIT", () => {
  let lote = "";
  let V1 = "";
  let V2 = "";
  let V7 = "";
  let sesion: Sesion;

  test("encabezados: «RUNT» y «SIMIT» centrados, subcolumnas sin prefijo visible y con nombre completo; columna «Acciones»", async ({ page }) => {
    exigirCliente();
    test.setTimeout(420_000);
    const prefijo = prefijoFicticio();
    [V1, V2, V7] = [placaFicticia(prefijo, "1"), placaFicticia(prefijo, "2"), placaFicticia(prefijo, "7")];
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, "/diagnosticos");
    sesion = await leerSesion(page);
    ({ diagnosticoId: lote } = await sembrarMasivo(page, [V1, V2, V7], { nombre: `E2E acciones ${Date.now()}` }));
    await esperarTerminados(page, lote, [V1, V2]);
    await page.reload();
    const tabla = grilla(page);
    await expect(tabla).toBeVisible();

    for (const grupo of ["RUNT", "SIMIT"]) {
      const th = tabla.locator('th[scope="colgroup"]').filter({ hasText: new RegExp(`^\\s*${grupo}\\s*$`) });
      await expect(th).toHaveCount(1);
      await expect(th).toBeVisible();
      expect(await th.evaluate((n) => getComputedStyle(n).textAlign), `${grupo}: text-align`).toBe("center");
      // RotuloConIcono con justify-center: el centro del contenido coincide con el centro de la celda (± 4 px).
      const [celda, contenido] = await th.evaluate((n) => {
        const r = n.getBoundingClientRect();
        const hijo = (n.firstElementChild ?? n) as HTMLElement;
        const range = document.createRange();
        range.selectNodeContents(hijo);
        const c = range.getBoundingClientRect();
        return [r.left + r.width / 2, c.left + c.width / 2];
      });
      expect(Math.abs(celda - contenido), `${grupo}: contenido centrado`).toBeLessThanOrEqual(4);
    }

    // Subcolumnas: visibles «vehículo» / «propietario» / «locatario»; el nombre accesible conserva el grupo (sr-only).
    for (const nombre of ["RUNT vehículo", "RUNT propietario", "RUNT locatario", "SIMIT vehículo", "SIMIT propietario", "SIMIT locatario"]) {
      const th = tabla.getByRole("columnheader", { name: nombre, exact: true });
      await expect(th, `columnheader «${nombre}»`).toHaveCount(1);
      const visible = await textoVisible(th);
      expect(visible, `texto visible de «${nombre}»`).toBe(nombre.split(" ")[1]);
      expect(await th.evaluate((n) => getComputedStyle(n).textAlign)).toMatch(/^(left|start)$/);
    }

    // Columna de acciones: siempre «Acciones» (desaparece «Reporte»).
    await expect(tabla.getByRole("columnheader", { name: "Acciones", exact: true })).toBeVisible();
    await expect(tabla.getByRole("columnheader", { name: "Reporte", exact: true })).toHaveCount(0);
    // Ya no hay botones apilados en la fila: un disparador por placa.
    await expect(disparadorAcciones(page, V1)).toHaveAttribute("aria-haspopup", "menu");
    await expect(disparadorAcciones(page, V1)).toHaveAttribute("aria-expanded", "false");

    await revisarTemasOla(page, "detalle-cabecera-1280", { captura: "A-1" });

    // Móvil (< 900 px, tarjetas): el data-label conserva el nombre completo de la fuente (spec-ui §2.6).
    await ancho(page, 360);
    const etiquetas = await tabla.locator("td[data-label]").evaluateAll((tds) => tds.map((t) => t.getAttribute("data-label")));
    expect(etiquetas).toEqual(expect.arrayContaining(["RUNT vehículo", "SIMIT vehículo"]));
    await sinDesbordeHorizontal(page, "detalle-360");
    await revisarTemasOla(page, "detalle-360", { captura: "A-1" });
    await ancho(page, 1280);
  });

  test("menú de una fila terminada: orden V2, solo las capacidades del rol y el menú no queda recortado por el scroll", async ({ page }) => {
    exigirCliente();
    test.skip(!lote, "Depende de la siembra del test anterior");
    test.setTimeout(180_000);
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, `/diagnosticos/${lote}`);
    await page.goto(`${APP}/diagnosticos/${lote}`);
    await expect(grilla(page)).toBeVisible();

    const { disparador, menu } = await abrirAcciones(page, V2);
    const nombres = await nombresItems(menu);
    test.info().annotations.push({ type: "ítems", description: nombres.join(" | ") });

    // Orden: cada ítem presente cae en una posición creciente de ORDEN_ITEMS.
    const posiciones = nombres.map((n) => ORDEN_ITEMS.findIndex((re) => re.test(n)));
    expect(posiciones, `ítems no reconocidos: ${nombres.join(" | ")}`).not.toContain(-1);
    expect([...posiciones].sort((a, b) => a - b)).toEqual(posiciones);

    // Sin la capacidad el ítem no existe; con la capacidad, existe (habilitado o con motivo).
    const c = sesion.capacidades;
    const tiene = (re: RegExp) => nombres.some((n) => re.test(n));
    expect(tiene(ORDEN_ITEMS[ITEM.saneamiento]), "saneamiento ↔ capacidad sanear").toBe(Boolean(c.sanear) || tiene(/^Ver saneamiento/));
    expect(tiene(ORDEN_ITEMS[ITEM.tramite]), "trámite ↔ capacidad derivar").toBe(c.derivar);
    expect(tiene(ORDEN_ITEMS[ITEM.pdf]), "PDF ↔ capacidad descargarReportes").toBe(c.descargarReportes);
    if (!c.reprocesarConsultas) expect(tiene(ORDEN_ITEMS[ITEM.reintentar]), "sin reprocesarConsultas no hay «Reintentar»").toBe(false);
    expect(tiene(ORDEN_ITEMS[ITEM.deuda]), "«Ver deuda» en una fila terminada").toBe(true);
    // Oleada 2026-11-16 (spec-ui §6.4): «Ver dictamen» abre el menú y «Auditoría del dictamen» solo con verAuditoria; un
    // separador divide el grupo Dictamen del grupo D-103.
    expect(tiene(ORDEN_ITEMS[ITEM.dictamen]), "«Ver dictamen» en el menú").toBe(true);
    expect(tiene(ORDEN_ITEMS[ITEM.auditoria]), "auditoría ↔ capacidad verAuditoria").toBe(Boolean(c.verAuditoria));
    await expect(menu.getByRole("separator")).toHaveCount(1);

    // El scroll horizontal de la grilla no recorta el menú: queda entero en el viewport y encima de todo.
    const caja = (await menu.boundingBox())!;
    const vista = page.viewportSize()!;
    expect(caja.x).toBeGreaterThanOrEqual(0);
    expect(caja.x + caja.width).toBeLessThanOrEqual(vista.width);
    expect(caja.y + caja.height).toBeLessThanOrEqual(vista.height + 1);
    const encima = await menu.evaluate((m) => {
      const r = m.getBoundingClientRect();
      const puntos = [
        [r.left + 4, r.top + 4],
        [r.right - 4, r.bottom - 4],
      ];
      return puntos.every(([x, y]) => m.contains(document.elementFromPoint(x, y)));
    });
    expect(encima, "el menú no está tapado ni recortado").toBe(true);

    await revisarTemasOla(page, "menu-acciones-1280", { captura: "A-2", paginaCompleta: false });
    await page.keyboard.press("Escape");
    await expect(menu).toHaveCount(0);
    await expect(disparador).toBeFocused();
  });

  test("teclado: Enter abre, ↓/↑/Home/End recorren (también los deshabilitados), Escape devuelve el foco y Tab cierra", async ({ page }) => {
    exigirCliente();
    test.skip(!lote, "Depende de la siembra del primer test");
    test.setTimeout(180_000);
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, `/diagnosticos/${lote}`);
    await page.goto(`${APP}/diagnosticos/${lote}`);
    const disparador = disparadorAcciones(page, V7);
    await disparador.focus();
    await page.keyboard.press("Enter");
    const menu = page.getByRole("menu");
    await expect(menu).toBeVisible();
    const items = menu.getByRole("menuitem");
    const n = await items.count();
    expect(n).toBeGreaterThan(0);
    await expect(items.first()).toBeFocused();
    await page.keyboard.press("End");
    await expect(items.last()).toBeFocused();
    await page.keyboard.press("Home");
    await expect(items.first()).toBeFocused();
    // ↓ recorre todos, incluidos los aria-disabled (spec-ui §2.2.3).
    for (let i = 1; i < n; i++) {
      await page.keyboard.press("ArrowDown");
      await expect(items.nth(i)).toBeFocused();
    }
    await page.keyboard.press("ArrowUp");
    await expect(items.nth(Math.max(0, n - 2))).toBeFocused();
    await page.keyboard.press("Escape");
    await expect(menu).toHaveCount(0);
    await expect(disparador).toBeFocused();
    await expect(disparador).toHaveAttribute("aria-expanded", "false");

    await page.keyboard.press("Space");
    await expect(menu).toBeVisible();
    await page.keyboard.press("Tab");
    await expect(menu).toHaveCount(0);
  });

  test("fila que no termina: «Descargar PDF» deshabilitado con motivo visible y anunciado; activarlo no hace nada", async ({ page }) => {
    exigirCliente();
    test.skip(!lote, "Depende de la siembra del primer test");
    test.setTimeout(180_000);
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, `/diagnosticos/${lote}`);
    await page.goto(`${APP}/diagnosticos/${lote}`);
    test.skip(!sesion?.capacidades.descargarReportes, "El usuario no tiene descargarReportes: el ítem PDF no existe");
    const { menu } = await abrirAcciones(page, V7);
    const pdf = menu.getByRole("menuitem", { name: /^Descargar PDF/ });
    await esperarDeshabilitadoConMotivo(pdf);
    // El motivo no lleva opacidad (contraste 4.5:1); el label sí (opacity-60).
    const motivoId = (await pdf.getAttribute("aria-describedby"))!.split(/\s+/)[0];
    expect(await page.locator(`[id="${motivoId}"]`).evaluate((n) => Number(getComputedStyle(n).opacity))).toBe(1);

    let descargo = false;
    page.once("download", () => (descargo = true));
    await pdf.focus();
    await page.keyboard.press("Enter");
    await page.waitForTimeout(1_000);
    expect(descargo, "un ítem aria-disabled no descarga").toBe(false);
    await expect(page.getByRole("dialog")).toHaveCount(0);

    // Saneamiento no elegible (no terminó): mismo patrón, con el motivo de ROTULO_MOTIVO_NO_ELEGIBLE.
    const sanear = menu.getByRole("menuitem", { name: /^Solicitar saneamiento/ });
    if (await sanear.count()) await esperarDeshabilitadoConMotivo(sanear);
    await revisarTemasOla(page, "menu-deshabilitado-1280", { captura: "A-3", paginaCompleta: false });
    await page.keyboard.press("Escape");
  });

  test("«Solicitar saneamiento» desde el menú: no hay botón de estado en la fila y el ítem pasa a «Quitar solicitud»", async ({ page }) => {
    exigirCliente();
    test.skip(!lote, "Depende de la siembra del primer test");
    test.skip(!sesion?.capacidades.sanear, "El usuario no tiene saneamiento.solicitar");
    test.setTimeout(180_000);
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, `/diagnosticos/${lote}`);
    await page.goto(`${APP}/diagnosticos/${lote}`);
    const fila = grilla(page).getByRole("row").filter({ has: page.getByRole("rowheader", { name: new RegExp(V1) }) });

    const { disparador, menu } = await abrirAcciones(page, V1);
    const solicitar = menu.getByRole("menuitem", { name: /^Solicitar saneamiento/ });
    test.skip((await solicitar.getAttribute("aria-disabled")) === "true", "La placa no es elegible para saneamiento en esta simulación");
    await solicitar.click();
    await expect(menu).toHaveCount(0);
    await expect(disparador).toBeFocused();
    // El estado no es un botón de la fila (antes «Sanear»/«Solicitado» con aria-pressed): lo dice el nombre del ítem.
    await expect(fila.getByRole("button", { name: /Solicitado/ })).toHaveCount(0);
    // El resultado se anuncia bajo el disparador (role=status) sin mover el foco.
    await expect.soft(fila.getByRole("status").first()).toBeVisible();

    const segundo = await abrirAcciones(page, V1);
    await expect(segundo.menu.getByRole("menuitem", { name: /^Quitar solicitud de saneamiento/ })).toBeVisible();
    await revisarTemasOla(page, "menu-saneamiento-solicitado-1280", { captura: "A-4", paginaCompleta: false });
    // Deja la fila como estaba.
    await segundo.menu.getByRole("menuitem", { name: /^Quitar solicitud de saneamiento/ }).click();
    await expect(segundo.menu).toHaveCount(0);
    await expect(async () => {
      const tercero = await abrirAcciones(page, V1);
      await expect(tercero.menu.getByRole("menuitem", { name: /^Solicitar saneamiento/ })).toBeVisible({ timeout: 2_000 });
    }).toPass({ timeout: 15_000, intervals: [1_000] });
    await disparador.click();
  });

  test("«Ver deuda» desde el menú abre el modal y al cerrarlo el foco vuelve a «Acciones» de esa fila", async ({ page }) => {
    exigirCliente();
    test.skip(!lote, "Depende de la siembra del primer test");
    test.setTimeout(180_000);
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, `/diagnosticos/${lote}`);
    await page.goto(`${APP}/diagnosticos/${lote}`);
    const { disparador, menu } = await abrirAcciones(page, V2);
    await menu.getByRole("menuitem", { name: /^Ver deuda/ }).click();
    const dialogo = page.getByRole("dialog", { name: "Deuda y saneamiento" });
    await expect(dialogo).toBeVisible();
    await dialogo.getByRole("button", { name: "Cerrar" }).click();
    await expect(dialogo).toHaveCount(0);
    await expect(disparador).toBeFocused();
  });

  test("fila sin ningún ítem: no hay disparador y se anuncia «Sin acciones para la placa X»", async ({ page }) => {
    exigirCliente();
    test.skip(!lote, "Depende de la siembra del primer test");
    test.setTimeout(180_000);
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, `/diagnosticos/${lote}`);
    // Variación de /sesion dentro del contrato: sin ninguna capacidad de acción de fila (solo lo que pinta el front).
    await variarCapacidades(page, { sanear: false, derivar: false, descargarReportes: false, reprocesarConsultas: false, verAuditoria: false });
    await page.goto(`${APP}/diagnosticos/${lote}`);
    await expect(grilla(page)).toBeVisible();
    // V7 no termina: sin capacidades no le queda «Ver deuda» (exige vehículo terminado) ni ningún otro ítem del grupo D-103.
    // Las filas terminadas pueden conservar «Ver deuda» (no depende de una capacidad), así que solo se mira V7.
    // Oleada 2026-11-16 (spec-ui §6.4): «Ver dictamen» está en el menú siempre que la fila tenga dictamen, así que V7 puede
    // conservar el disparador solo con el grupo Dictamen. Sin dictamen, vuelve el «Sin acciones…» sr-only.
    if (await disparadorAcciones(page, V7).count()) {
      const { menu } = await abrirAcciones(page, V7);
      const nombres = await nombresItems(menu);
      expect(nombres.every((n) => ORDEN_ITEMS[ITEM.dictamen].test(n)), `V7 sin capacidades solo tiene «Ver dictamen»: ${nombres.join(" | ")}`).toBe(true);
      await page.keyboard.press("Escape");
    } else {
      const sinAcciones = page.getByText(`Sin acciones para la placa ${V7}`, { exact: true });
      await expect(sinAcciones).toHaveCount(1);
      await expect(sinAcciones).toHaveClass(/sr-only/);
    }
  });
});
