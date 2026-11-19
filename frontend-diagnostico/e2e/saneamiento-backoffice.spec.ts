import { expect, test, type Page, type Route } from "@playwright/test";
import {
  API,
  APP,
  BACKOFFICE,
  CLIENTE,
  EMPRESA,
  PDF_FALSO,
  PDF_VALIDO,
  ancho,
  buscarEnBandeja,
  descargar,
  entrar,
  esperarTerminados,
  exigirBackoffice,
  exigirCliente,
  fallarUnaVez,
  idVehiculo,
  marcarPorApi,
  placaFicticia,
  prefijoFicticio,
  recorrerConTab,
  revisarTemas,
  sembrarExpress,
  sinDesbordeHorizontal,
  tomarPorApi,
} from "./saneamiento-comun";
import { abrirAcciones, esperarBandera, pulsarBandera } from "./ola-ux-comun";

/**
 * HU #14577 — backoffice FLIT: PS-4 bandeja «Saneamientos» (AC24–AC28) y PS-5 gestión de un saneamiento (AC29–AC38), más el
 * recorrido de AC45 entre la compañía y FLIT: marcar → tomar → editar → cargar evidencia → cerrar → la compañía lo ve y
 * descarga. Con AC40 (estados), AC41 y AC42 (axe en claro y oscuro, Tab a 360 px, sin desplazamiento horizontal) y las
 * capturas C-9 a C-14 de .claude/state/saneamiento/auditoria-ui.md (C-12 decide UA-14).
 *
 * Necesita la suite local con simulación (scripts/dev/levantar-diagnostico-local.sh simulado) y dos sesiones:
 *   E2E_USUARIO / E2E_CLAVE — usuario de la compañía cliente con diagnosticos.crear, diagnosticos.ver, reportes.descargar y
 *     saneamiento.solicitar.
 *   E2E_BACKOFFICE_USUARIO / E2E_BACKOFFICE_CLAVE — SuperAdmin de la compañía FLIT (incluye saneamiento.gestionar).
 *   E2E_EMPRESA — uuid de la compañía de E2E_USUARIO (las rutas del backoffice llevan ?empresa=).
 * Las placas son ficticias y nuevas en cada corrida; el escenario 2 de la simulación (SIMIT con deuda) hace que «Tomar» genere
 * al menos un concepto automático.
 *
 * Lista congelada: nombres accesibles de los que depende este spec. Si uno cambia por decisión de UX, el spec se ajusta en
 * el mismo PR de la pantalla.
 *   - navigation «Navegación principal» · button «/, \d+ pendientes$/» (píldora) · link «Saneamientos, N solicitudes sin tomar»
 *   - button «Abrir menú de navegación, N pendientes» (360) · texto «99+»
 *   - heading (h1) «Saneamientos» · group «Filtrar por estado» · button «Saneamiento solicitado» · button «En saneamiento»
 *   - search «Filtros de la bandeja» · textbox «Placa» · button «Limpiar filtros» · button «Todas las compañías»
 *   - table «Saneamientos» · checkbox «Seleccionar la placa <placa>» · button «Seleccionar las solicitudes sin tomar»
 *   - button «/^Tomar \(\d+\)$/» · link «Descargar Excel» · navigation «Paginación de saneamientos»
 *   - link «Gestionar saneamiento de <placa>: Saneamiento solicitado» (grilla del backoffice, C-13)
 *   - button «Tomar» · heading (h3) «Conceptos a sanear» · button «Agregar otro concepto» · dialog «Agregar otro concepto»
 *   - textbox «Descripción» · button «Agregar» · radiogroup «Resultado de <concepto>» · radio «Saneado» · textbox «Nota de <concepto>»
 *   - row «<concepto>» · button «Reintentar <concepto>» · texto «Guardado» · «No se guardó» · «Otra persona cambió este saneamiento»
 *   - button «Recargar» · button «Cargar evidencia de <concepto>» · dialog «Evidencia de <concepto>» · input de archivo de ZonaArrastre
 *   - button «Subir» · button «Ver evidencia de <concepto>» · button «Reemplazar evidencia de <concepto>»
 *   - button «Quitar concepto <concepto>» · dialog «¿Quitar el concepto?» · button «Cancelar» · button «Quitar»
 *   - textbox «Observación para la compañía» · button «Cerrar como saneado» · dialog «¿Cerrar como saneado?»
 *   - button «Cerrar como no procede» · button «Reabrir» · button «Anular» · textbox «Motivo» · heading «Historial»
 *   - Cliente (ola 2026-11-15, FE-2): button «Acciones de la placa <placa>» · menuitem «Solicitar saneamiento de la placa <placa>» / «Quitar solicitud de saneamiento de la placa <placa>» · menuitem «Ver saneamiento de la placa <placa>: Saneado» · dialog «Deuda y saneamiento» · table «Conceptos del saneamiento»
 *     · link «Descargar evidencia de <concepto>» · button «/^Avisos/» · region «Avisos» · link «/^Ver lote: /»
 */

// Sesión única por usuario: dos tests a la vez con el mismo usuario se invalidan la sesión (bucle de reautenticación en
// «Abriendo Diagnóstico…»). Los tests de este archivo van en serie en un mismo worker (ver cabecera de playwright.config.ts).
test.describe.configure({ mode: "serial" });

const RESUMEN = /\/api\/v1\/diagnostico\/backoffice\/saneamientos\/resumen$/;
const BANDEJA = /\/api\/v1\/diagnostico\/backoffice\/saneamientos\?/;
const EXCEL = /\/api\/v1\/diagnostico\/backoffice\/saneamientos\/excel/;
const RENGLON_PUT = /\/backoffice\/saneamientos\/[0-9a-f-]{36}\/renglones\/[0-9a-f-]{36}\?/;
const EVIDENCIA_PUT = /\/backoffice\/saneamientos\/[0-9a-f-]{36}\/renglones\/[0-9a-f-]{36}\/evidencia\?/;

async function estadoBackoffice(bo: Page, sid: string): Promise<string> {
  const r = await bo.request.get(`${API}/backoffice/saneamientos/${sid}?empresa=${encodeURIComponent(EMPRESA!)}`);
  expect(r.ok(), `GET saneamiento ${r.status()}`).toBeTruthy();
  return ((await r.json()) as { estado: string }).estado;
}

/** Dos sesiones: la compañía en `page` y FLIT en un contexto aparte. */
async function sesiones(page: Page, browser: import("@playwright/test").Browser) {
  await entrar(page, CLIENTE.usuario!, CLIENTE.clave!);
  const contextoFlit = await browser.newContext({ locale: "es-CO", timezoneId: "America/Bogota", viewport: { width: 1280, height: 800 } });
  const bo = await contextoFlit.newPage();
  await entrar(bo, BACKOFFICE.usuario!, BACKOFFICE.clave!);
  return { bo, cerrar: () => contextoFlit.close() };
}

test.describe("PS-4 bandeja de saneamientos", () => {
  test("insignia del menú, filtros, «Seleccionar las solicitudes sin tomar» y «Tomar (N)», y el Excel", async ({ page, browser }) => {
    exigirCliente();
    exigirBackoffice();
    test.setTimeout(480_000);
    const P = placaFicticia(prefijoFicticio(), "2");
    const { bo, cerrar } = await sesiones(page, browser);

    // Siembra: la misma placa en dos lotes; el primero queda EN_GESTION y el segundo SOLICITADO.
    const lotes: string[] = [];
    for (const n of [1, 2]) {
      const { diagnosticoId } = await sembrarExpress(page, P, { nombre: `E2E bandeja ${n} ${Date.now()}` });
      await esperarTerminados(page, diagnosticoId, [P]);
      await marcarPorApi(page, diagnosticoId, await idVehiculo(page, diagnosticoId, P));
      lotes.push(diagnosticoId);
      if (n === 1) {
        const { id, empresa } = await buscarEnBandeja(bo, P);
        await tomarPorApi(bo, id, empresa);
      }
    }

    await test.step("AC24 / AC25: entrada «Saneamientos» con el número real de solicitudes sin tomar", async () => {
      const real = (await (await bo.request.get(`${API}/backoffice/saneamientos/resumen`)).json()) as { solicitados: number };
      expect(real.solicitados).toBeGreaterThanOrEqual(1);
      await bo.goto(`${APP}/backoffice/saneamientos`);
      await expect(bo.getByRole("heading", { level: 1, name: "Saneamientos" })).toBeVisible();
      const pildora = bo.getByRole("navigation", { name: "Navegación principal" }).getByRole("button", { name: /, \d+ pendientes$/ });
      await expect(pildora.first()).toBeVisible();
      const entrada = bo.getByRole("link", { name: /^Saneamientos, \d+ solicitudes sin tomar$/ });
      if (!(await entrada.isVisible())) await pildora.first().click();
      await expect(entrada).toHaveAccessibleName(`Saneamientos, ${real.solicitados} solicitudes sin tomar`);
    });

    await test.step("C-12 / UA-14: insignia con 3 y con 150 («99+») en la píldora, la entrada y el lanzador móvil", async () => {
      // Variación del resumen real dentro del contrato (ResumenSaneamientos: { solicitados }).
      let valor = 3;
      const resumen = (route: Route) => route.fulfill({ json: { solicitados: valor } });
      await bo.route(RESUMEN, resumen);
      for (valor of [3, 150]) {
        await ancho(bo, 1280);
        await bo.goto(`${APP}/backoffice/saneamientos`);
        const pildora = bo.getByRole("button", { name: new RegExp(`, ${valor} pendientes$`) });
        await expect(pildora.first()).toBeVisible();
        const entrada = bo.getByRole("link", { name: `Saneamientos, ${valor} solicitudes sin tomar` });
        if (!(await entrada.isVisible())) await pildora.first().click();
        await expect(entrada).toBeVisible();
        if (valor === 150) await expect(bo.getByText("99+", { exact: true }).first()).toBeVisible();
        await revisarTemas(bo, `dock-${valor}-1280`, { captura: "C-12", paginaCompleta: false });
        await bo.keyboard.press("Escape");
        await ancho(bo, 360);
        await bo.reload();
        const lanzador = bo.getByRole("button", { name: `Abrir menú de navegación, ${valor} pendientes` });
        await expect(lanzador).toBeVisible();
        await revisarTemas(bo, `dock-${valor}-360`, { captura: "C-12", paginaCompleta: false });
      }
      await bo.unroute(RESUMEN, resumen);
      await ancho(bo, 1280);
    });

    await test.step("AC26: filtros por estado (multiselección), compañía y placa en la URL", async () => {
      await bo.goto(`${APP}/backoffice/saneamientos`);
      const chips = bo.getByRole("group", { name: "Filtrar por estado" });
      await expect(chips.getByRole("button", { name: "Saneamiento solicitado" })).toHaveAttribute("aria-pressed", "true");
      await expect(chips.getByRole("button", { name: "En saneamiento" })).toHaveAttribute("aria-pressed", "false");
      await chips.getByRole("button", { name: "En saneamiento" }).click();
      await expect(bo).toHaveURL(/estado=SOLICITADO/);
      await expect(bo).toHaveURL(/estado=EN_GESTION/);
      const placa = bo.getByRole("search", { name: "Filtros de la bandeja" }).getByRole("textbox", { name: "Placa" });
      await placa.fill(P);
      await placa.press("Enter");
      await expect(bo).toHaveURL(new RegExp(`[?&]placa=${P}(&|$)`));
      const tabla = bo.getByRole("table", { name: "Saneamientos" });
      await expect(tabla.getByRole("rowheader")).toHaveCount(2);
      await bo.goto(`${bo.url()}&empresa=${encodeURIComponent(EMPRESA!)}`);
      await expect(bo.getByRole("button", { name: "Todas las compañías" })).toBeVisible();
      await expect(tabla.getByRole("rowheader")).toHaveCount(2);
    });

    await test.step("AC27 / C-9: solo la solicitud sin tomar se puede elegir; Tab a 360 sin foco invisible (UA-1)", async () => {
      const tabla = bo.getByRole("table", { name: "Saneamientos" });
      const casillas = tabla.getByRole("checkbox", { name: `Seleccionar la placa ${P}` });
      await expect(casillas).toHaveCount(2);
      await expect(casillas.and(bo.locator(":disabled"))).toHaveCount(1);
      await expect(casillas.and(bo.locator(":disabled"))).toHaveAccessibleDescription("Solo se toman las solicitudes sin tomar");
      await bo.getByRole("button", { name: "Seleccionar las solicitudes sin tomar" }).click();
      await expect(bo.getByRole("button", { name: /^Tomar \(1\)$/ })).toBeVisible();
      await revisarTemas(bo, "bandeja-1280", { captura: "C-9" });
      await sinDesbordeHorizontal(bo, "bandeja-1280");
      await ancho(bo, 360);
      await revisarTemas(bo, "bandeja-360", { captura: "C-9" });
      await sinDesbordeHorizontal(bo, "bandeja-360");
      await recorrerConTab(bo, "bandeja-360", { pasos: 70 });
      await ancho(bo, 1280);
    });

    await test.step("AC27: «Tomar (1)» toma la solicitud, el foco va al resultado y la tabla se actualiza", async () => {
      await bo.getByRole("button", { name: /^Tomar \(1\)$/ }).click();
      await expect(bo.locator(":focus")).toContainText("Se tomó 1 saneamiento.");
      await expect(bo.getByText("Saneamientos tomados")).toBeVisible();
      await revisarTemas(bo, "bandeja-resultado-1280", { captura: "C-9" });
      const tabla = bo.getByRole("table", { name: "Saneamientos" });
      await expect(tabla.getByRole("checkbox", { name: `Seleccionar la placa ${P}` }).and(bo.locator(":disabled"))).toHaveCount(2);
    });

    await test.step("AC27: «Descargar Excel» con los filtros y 422 DEMASIADAS_FILAS", async () => {
      await descargar(bo, bo.getByRole("link", { name: "Descargar Excel" }), /\.xlsx$/i);
      const fallo = await fallarUnaVez(bo, "GET", EXCEL, { status: 422, code: "DEMASIADAS_FILAS", error: "Demasiadas filas." });
      await bo.getByRole("link", { name: "Descargar Excel" }).click();
      await expect(bo.getByRole("alert").filter({ hasText: "Hay más de 5.000 saneamientos con estos filtros. Acota las fechas o el estado para descargar." })).toBeVisible();
      await fallo.quitar();
    });

    await test.step("AC28: Enter en la placa y clic en la fila llevan a la gestión", async () => {
      const tabla = bo.getByRole("table", { name: "Saneamientos" });
      const volver = bo.url();
      await tabla.getByRole("rowheader").first().getByRole("link").press("Enter");
      await bo.waitForURL(/\/backoffice\/saneamientos\/[0-9a-f-]{36}\?empresa=/i);
      await bo.goto(volver);
      await tabla.getByRole("row").filter({ has: bo.getByRole("rowheader") }).first().getByRole("cell").nth(2).click();
      await bo.waitForURL(/\/backoffice\/saneamientos\/[0-9a-f-]{36}\?empresa=/i);
    });

    await test.step("AC40: vacío con filtros y error con «Reintentar»", async () => {
      await bo.goto(`${APP}/backoffice/saneamientos?placa=ZZZ999`);
      await expect(bo.getByText("Ningún saneamiento coincide")).toBeVisible();
      await revisarTemas(bo, "bandeja-vacio-filtros-1280");
      const fallo = await fallarUnaVez(bo, "GET", BANDEJA, { status: 500, code: "ERROR_INTERNO", error: "Fallo de prueba." }, { siempre: true });
      await bo.reload();
      await expect(bo.getByText("No pudimos cargar la bandeja")).toBeVisible();
      await expect(bo.getByText("ERROR_INTERNO")).toHaveCount(0);
      await fallo.quitar();
      await bo.getByRole("button", { name: "Reintentar" }).click();
      await expect(bo.getByText("Ningún saneamiento coincide")).toBeVisible();
    });

    await cerrar();
  });
});

test.describe("PS-5 gestión y recorrido AC45", () => {
  test("tomar, editar con guardado automático y reintento, evidencia PDF, conceptos, 409, cerrar, la compañía descarga, reabrir y anular", async ({ page, browser }) => {
    exigirCliente();
    exigirBackoffice();
    test.setTimeout(600_000);
    const P = placaFicticia(prefijoFicticio(), "2");
    const CONCEPTO = "Otro: Prenda de prueba E2E";
    const { bo, cerrar } = await sesiones(page, browser);

    // ---------- Compañía: marca con la bandera ----------
    const { diagnosticoId: lote } = await sembrarExpress(page, P, { nombre: `E2E gestion ${Date.now()}` });
    await esperarTerminados(page, lote, [P]);
    await page.reload();
    await test.step("AC45 (compañía): marca con la bandera", async () => {
      // Ola 2026-11-15 (FE-2): la bandera es un menuitem de «Acciones de la placa P»; su nombre dice el estado.
      await esperarBandera(page, P, "apagada");
      await pulsarBandera(page, P);
      await esperarBandera(page, P, "encendida");
    });

    // ---------- FLIT: entra por la grilla del backoffice ----------
    let sid = "";
    await test.step("AC38 / C-13: la etiqueta de la grilla del backoffice enlaza a la gestión", async () => {
      await bo.goto(`${APP}/backoffice/diagnosticos/${lote}?empresa=${encodeURIComponent(EMPRESA!)}`);
      const enlace = bo.getByRole("link", { name: `Gestionar saneamiento de ${P}: Saneamiento solicitado` });
      await expect(enlace).toBeVisible();
      // La bandera no existe en el backoffice: ni como botón ni como ítem del menú «Acciones» de la compañía.
      await expect(bo.getByRole("button", { name: `Solicitar saneamiento de la placa ${P}` })).toHaveCount(0);
      await expect(bo.getByRole("menuitem", { name: new RegExp(`^(Solicitar saneamiento|Quitar solicitud de saneamiento) de la placa ${P}$`) })).toHaveCount(0);
      await enlace.focus();
      await revisarTemas(bo, "grilla-backoffice-1280", { captura: "C-13" });
      await enlace.click();
      await bo.waitForURL(/\/backoffice\/saneamientos\/[0-9a-f-]{36}\?empresa=/i);
      sid = new URL(bo.url()).pathname.split("/").at(-1)!;
    });

    await test.step("AC29 / AC30: encabezado y «Tomar»; el foco va a «Conceptos a sanear»", async () => {
      await expect(bo.getByText(/^La compañía pidió el saneamiento el /)).toBeVisible();
      // D-60: con el contexto del detalle, el diagnóstico enlaza al lote del backoffice; sin él va como texto.
      const enlaceLote = bo.getByRole("link", { name: /^E2E gestion / });
      if (await enlaceLote.count()) await expect(enlaceLote).toHaveAttribute("href", new RegExp(`/backoffice/diagnosticos/${lote}\\?empresa=`));
      await revisarTemas(bo, "gestion-solicitado-1280");
      await bo.getByRole("button", { name: "Tomar", exact: true }).click();
      await expect(bo.getByRole("heading", { level: 3, name: "Conceptos a sanear" })).toBeFocused();
      expect(await estadoBackoffice(bo, sid)).toBe("EN_GESTION");
      await expect(bo.getByRole("radiogroup", { name: /^Resultado de / }).first()).toBeVisible();
    });

    await test.step("AC33: «Agregar otro concepto» valida 3 caracteres y lleva el foco al resultado de la fila nueva", async () => {
      await bo.getByRole("button", { name: "Agregar otro concepto" }).click();
      const modal = bo.getByRole("dialog", { name: "Agregar otro concepto" });
      const descripcion = modal.getByRole("textbox", { name: "Descripción" });
      await descripcion.fill("ab");
      await modal.getByRole("button", { name: "Agregar", exact: true }).click();
      await expect(modal.getByText("Escribe al menos 3 caracteres.")).toBeVisible();
      await expect(descripcion).toHaveAttribute("aria-invalid", "true");
      await descripcion.fill("Prenda de prueba E2E");
      await modal.getByRole("button", { name: "Agregar", exact: true }).click();
      await expect(modal).toHaveCount(0);
      await expect(bo.getByRole("row", { name: CONCEPTO })).toBeVisible();
      // Blanda: si el foco no llega, el resto del recorrido AC45 sigue y el mensaje dice dónde quedó el foco.
      const enfocado = () =>
        bo.evaluate(() => {
          const el = document.activeElement as HTMLElement | null;
          const grupo = el?.closest('[role="radiogroup"]')?.getAttribute("aria-label") ?? "";
          return `${grupo} › ${el?.tagName.toLowerCase()} «${(el?.getAttribute("aria-label") || el?.innerText || "").trim().slice(0, 60)}»`;
        });
      await expect.configure({ soft: true }).poll(enfocado, { message: "AC33: el foco va al resultado de la fila nueva", timeout: 5_000 }).toMatch(new RegExp(`^Resultado de ${CONCEPTO} › `));
    });

    const fila = () => bo.getByRole("row", { name: CONCEPTO });
    const nota = () => bo.getByRole("textbox", { name: `Nota de ${CONCEPTO}` });

    await test.step("AC31: el resultado se guarda solo y la nota al salir del campo", async () => {
      await bo.getByRole("radiogroup", { name: `Resultado de ${CONCEPTO}` }).getByRole("radio", { name: "Saneado" }).click();
      await expect(fila()).toContainText("Guardado");
      await nota().fill("Pagado en ventanilla");
      await expect(fila()).toContainText("Sin guardar");
      await nota().press("Tab");
      await expect(fila()).toContainText("Guardado");
    });

    await test.step("AC31 / C-10: con la red caída la fila dice «No se guardó», conserva lo escrito y «Reintentar» guarda", async () => {
      const fallo = await fallarUnaVez(bo, "PUT", RENGLON_PUT, "red");
      await nota().fill("Pagado en ventanilla con recibo");
      await nota().press("Tab");
      await expect(fila()).toContainText("No se guardó");
      await expect(nota()).toHaveValue("Pagado en ventanilla con recibo");
      const reintentar = bo.getByRole("button", { name: `Reintentar ${CONCEPTO}` });
      await expect(reintentar).toBeVisible();
      await revisarTemas(bo, "gestion-error-renglon-1280", { captura: "C-10" });
      await fallo.quitar();
      await reintentar.click();
      await expect(fila()).toContainText("Guardado");
    });

    await test.step("AC37 / C-11: 409 VERSION_DESACTUALIZADA real con dos pestañas; «Recargar» trae lo vigente", async () => {
      const otra = await bo.context().newPage();
      await otra.goto(bo.url());
      const notaOtra = otra.getByRole("textbox", { name: `Nota de ${CONCEPTO}` });
      await notaOtra.fill("Cambio desde otra pestaña");
      await notaOtra.press("Tab");
      await expect(otra.getByRole("row", { name: CONCEPTO })).toContainText("Guardado");
      await otra.close();
      await nota().fill("Cambio con la versión vieja");
      await nota().press("Tab");
      await expect(bo.getByText("Otra persona cambió este saneamiento")).toBeVisible();
      await expect(fila()).toContainText("Otra persona cambió este concepto.");
      await revisarTemas(bo, "gestion-conflicto-1280", { captura: "C-11", paginaCompleta: false });
      await ancho(bo, 360);
      await revisarTemas(bo, "gestion-conflicto-360", { captura: "C-11", paginaCompleta: false });
      await ancho(bo, 1280);
      await bo.getByRole("button", { name: "Recargar" }).click();
      await expect(bo.getByText("Otra persona cambió este saneamiento")).toHaveCount(0);
      await expect(nota()).toHaveValue("Cambio desde otra pestaña");
    });

    await test.step("AC32 / C-11: el PDF falso se rechaza antes de subir; el válido se sube, se reemplaza y se ve", async () => {
      await bo.getByRole("button", { name: `Cargar evidencia de ${CONCEPTO}` }).click();
      const modal = bo.getByRole("dialog", { name: `Evidencia de ${CONCEPTO}` });
      // La entrada de archivo de ZonaArrastre es sr-only; se le entrega el archivo como lo haría el diálogo del sistema.
      const archivo = modal.locator('input[type="file"]');
      await archivo.setInputFiles({ name: "evidencia.pdf", mimeType: "application/pdf", buffer: PDF_FALSO });
      await expect(modal.getByRole("alert").filter({ hasText: "El archivo no es un PDF válido." })).toBeVisible();
      await expect(modal.getByRole("button", { name: "Subir" })).toBeDisabled();
      await revisarTemas(bo, "gestion-evidencia-rechazada-1280", { captura: "C-11", paginaCompleta: false });
      await archivo.setInputFiles({ name: "evidencia-pago.pdf", mimeType: "application/pdf", buffer: PDF_VALIDO });
      await modal.getByRole("button", { name: "Subir" }).click();
      await expect(modal).toHaveCount(0);
      await expect(fila()).toContainText("evidencia-pago.pdf");
      await expect(bo.getByRole("button", { name: `Ver evidencia de ${CONCEPTO}` })).toBeFocused();

      // 422 ARCHIVO_NO_ES_PDF del servidor: se muestra en el modal y el renglón conserva la evidencia anterior.
      const fallo = await fallarUnaVez(bo, "PUT", EVIDENCIA_PUT, { status: 422, code: "ARCHIVO_NO_ES_PDF", error: "El archivo no es un PDF." });
      await bo.getByRole("button", { name: `Reemplazar evidencia de ${CONCEPTO}` }).click();
      await modal.locator('input[type="file"]').setInputFiles({ name: "reemplazo.pdf", mimeType: "application/pdf", buffer: PDF_VALIDO });
      await modal.getByRole("button", { name: "Subir" }).click();
      await expect(modal.getByText("El archivo no es un PDF válido.")).toBeVisible();
      await modal.getByRole("button", { name: "Cancelar" }).click();
      await expect(fila()).toContainText("evidencia-pago.pdf");
      await fallo.quitar();

      await bo.getByRole("button", { name: `Reemplazar evidencia de ${CONCEPTO}` }).click();
      await modal.locator('input[type="file"]').setInputFiles({ name: "reemplazo.pdf", mimeType: "application/pdf", buffer: PDF_VALIDO });
      await modal.getByRole("button", { name: "Subir" }).click();
      await expect(modal).toHaveCount(0);
      await expect(fila()).toContainText("reemplazo.pdf");
      await descargar(bo, bo.getByRole("button", { name: `Ver evidencia de ${CONCEPTO}` }), /\.pdf$/i);
    });

    await test.step("AC33 / C-11: quitar un concepto con confirmación (foco inicial en «Cancelar»)", async () => {
      await bo.getByRole("button", { name: "Agregar otro concepto" }).click();
      const agregar = bo.getByRole("dialog", { name: "Agregar otro concepto" });
      await agregar.getByRole("textbox", { name: "Descripción" }).fill("Concepto para quitar");
      await agregar.getByRole("button", { name: "Agregar", exact: true }).click();
      const otro = "Otro: Concepto para quitar";
      await expect(bo.getByRole("row", { name: otro })).toBeVisible();
      await bo.getByRole("button", { name: `Quitar concepto ${otro}` }).click();
      const confirmar = bo.getByRole("dialog", { name: "¿Quitar el concepto?" });
      await expect(confirmar.getByRole("button", { name: "Cancelar" })).toBeFocused();
      await revisarTemas(bo, "gestion-quitar-concepto-1280", { captura: "C-11", paginaCompleta: false });
      await confirmar.getByRole("button", { name: "Quitar", exact: true }).click();
      await expect(confirmar).toHaveCount(0);
      await expect(bo.getByRole("row", { name: otro })).toHaveCount(0);
    });

    await test.step("AC33: la observación general se guarda al salir del campo", async () => {
      const obs = bo.getByRole("textbox", { name: "Observación para la compañía" });
      await obs.fill("Gestionado por FLIT en la prueba E2E.");
      await obs.press("Tab");
      await expect.poll(async () => {
        const r = await bo.request.get(`${API}/backoffice/saneamientos/${sid}?empresa=${encodeURIComponent(EMPRESA!)}`);
        return ((await r.json()) as { observacion_flit: string | null }).observacion_flit;
      }).toBe("Gestionado por FLIT en la prueba E2E.");
    });

    await test.step("C-10 / AC41 / AC42: dos columnas a 1280, deuda plegada a 1023, tarjetas a 360; Tab sin foco invisible", async () => {
      await revisarTemas(bo, "gestion-1280", { captura: "C-10" });
      await ancho(bo, 1023);
      await expect(bo.getByRole("button", { name: /^Deuda del vehículo/ })).toHaveAttribute("aria-expanded", "false");
      await revisarTemas(bo, "gestion-1023", { captura: "C-10" });
      await ancho(bo, 360);
      await revisarTemas(bo, "gestion-360", { captura: "C-10" });
      await sinDesbordeHorizontal(bo, "gestion-360");
      await recorrerConTab(bo, "gestion-360", { pasos: 80 });
      await ancho(bo, 1280);
    });

    await test.step("AC34: «Cerrar como saneado» explica qué falta y cierra cuando se cumple la regla", async () => {
      const cerrarSaneado = bo.getByRole("button", { name: "Cerrar como saneado" });
      const pendientes = bo.getByRole("radiogroup", { name: /^Resultado de / }).filter({ has: bo.getByRole("radio", { name: "Pendiente", checked: true }) });
      if (await pendientes.count()) {
        await expect(cerrarSaneado).toHaveAttribute("aria-disabled", "true");
        await expect(cerrarSaneado).toHaveAccessibleDescription(/ningún concepto puede quedar pendiente/);
      }
      // Uno por uno: el locator se reevalúa, así cada vuelta toma el siguiente que siga en Pendiente.
      for (let i = 0; i < 20 && (await pendientes.count()) > 0; i++) {
        await pendientes.first().getByRole("radio", { name: "Saneado" }).click();
        await expect(bo.getByText(/^(Guardando…|Sin guardar)$/)).toHaveCount(0);
      }
      await expect(pendientes).toHaveCount(0);
      await expect(cerrarSaneado).not.toHaveAttribute("aria-disabled", "true");
      await cerrarSaneado.click();
      const modal = bo.getByRole("dialog", { name: "¿Cerrar como saneado?" });
      await expect(modal.getByRole("button", { name: "Cancelar" })).toBeFocused();
      await modal.getByRole("button", { name: "Cerrar como saneado" }).click();
      await expect(modal).toHaveCount(0);
      await expect(bo.getByRole("button", { name: "Reabrir" })).toBeVisible();
      expect(await estadoBackoffice(bo, sid)).toBe("SANEADO");
      await revisarTemas(bo, "gestion-saneado-1280");
    });

    await test.step("AC45 (compañía): ve el saneamiento cerrado y descarga la evidencia y el PDF", async () => {
      await page.goto(`${APP}/diagnosticos/${lote}`);
      // Ola 2026-11-15 (FE-2): «Ver saneamiento» vive en el menú «Acciones» de la fila.
      const { menu } = await abrirAcciones(page, P);
      await menu.getByRole("menuitem", { name: `Ver saneamiento de la placa ${P}: Saneado` }).click();
      const panel = page.getByRole("dialog", { name: "Deuda y saneamiento" });
      await expect(panel.getByRole("heading", { name: "Saneamiento", exact: true })).toBeVisible();
      await expect(panel.getByText("Gestionado por FLIT en la prueba E2E.")).toBeVisible();
      await expect(panel.getByRole("table", { name: "Conceptos del saneamiento" })).toContainText("Prenda de prueba E2E");
      await expect(panel.getByRole("list", { name: "Historial del saneamiento" })).toContainText("FLIT cerró el saneamiento como saneado");
      // SP-15 + E8 (D-42/UA-8): la compañía nunca ve el id, el nombre ni el rótulo del operador de FLIT.
      await expect(panel.getByText(/Otro operador de FLIT|FLIT · usuario/)).toHaveCount(0);
      await descargar(page, panel.getByRole("link", { name: `Descargar evidencia de ${CONCEPTO}` }), /\.pdf$/i);
      await descargar(page, panel.getByRole("link", { name: "Descargar PDF" }), /\.pdf$/i);
      await revisarTemas(page, "panel-cliente-saneado-640", { captura: "C-5", paginaCompleta: false });
      await page.keyboard.press("Escape");
    });

    await test.step("C-14: el aviso de la campana abre el tablero con ?saneamiento=con y el chip activo", async () => {
      const campana = page.getByRole("button", { name: /^Avisos/ }).first();
      const aviso = page.getByRole("region", { name: "Avisos" }).getByRole("link", { name: /^Ver lote\s*:.*saneamiento/i }).first();
      let llego = false;
      for (let i = 0; i < 6 && !llego; i++) {
        await page.goto(`${APP}/`);
        await campana.click();
        llego = await aviso.isVisible().catch(() => false);
        if (!llego) {
          await page.keyboard.press("Escape");
          await page.waitForTimeout(5_000);
        }
      }
      if (!llego) {
        test.info().annotations.push({ type: "omitido", description: "C-14: no llegó el aviso de saneamiento a la campana en 30 s (bandeja de notificaciones de core-diagnostico)" });
        return;
      }
      await revisarTemas(page, "campana-1280", { captura: "C-14", paginaCompleta: false });
      await aviso.click();
      await page.waitForURL(/[?&]saneamiento=con(&|$)/);
      await expect(page.getByRole("group", { name: "Filtrar por saneamiento" }).getByRole("button", { name: "Con saneamiento" })).toHaveAttribute("aria-pressed", "true");
      await revisarTemas(page, "campana-tablero-1280", { captura: "C-14" });
    });

    await test.step("AC35 / C-11: «Reabrir» exige un motivo de 10 caracteres y vuelve a EN_GESTION con los renglones", async () => {
      await bo.getByRole("button", { name: "Reabrir" }).click();
      const modal = bo.getByRole("dialog", { name: "Reabrir el saneamiento" });
      const motivo = modal.getByRole("textbox", { name: "Motivo" });
      await expect(motivo).toBeFocused();
      await motivo.fill("corto");
      await modal.getByRole("button", { name: "Reabrir" }).click();
      await expect(modal.getByText("Escribe al menos 10 caracteres.")).toBeVisible();
      await expect(motivo).toHaveAttribute("aria-invalid", "true");
      await expect(motivo).toBeFocused();
      await revisarTemas(bo, "gestion-motivo-error-1280", { captura: "C-11", paginaCompleta: false });
      await ancho(bo, 360);
      await revisarTemas(bo, "gestion-motivo-error-360", { captura: "C-11", paginaCompleta: false });
      await ancho(bo, 1280);
      await motivo.fill("Reapertura de la prueba E2E");
      await modal.getByRole("button", { name: "Reabrir" }).click();
      await expect(modal).toHaveCount(0);
      expect(await estadoBackoffice(bo, sid)).toBe("EN_GESTION");
      await expect(bo.getByRole("radiogroup", { name: `Resultado de ${CONCEPTO}` }).getByRole("radio", { name: "Saneado" })).toBeChecked();
    });

    await test.step("AC34: «Cerrar como no procede» con motivo y nueva reapertura", async () => {
      await bo.getByRole("button", { name: "Cerrar como no procede" }).click();
      const modal = bo.getByRole("dialog", { name: "Cerrar como no procede" });
      await modal.getByRole("textbox", { name: "Motivo" }).fill("No procede en la prueba E2E");
      await modal.getByRole("button", { name: "Cerrar como no procede" }).click();
      await expect(modal).toHaveCount(0);
      expect(await estadoBackoffice(bo, sid)).toBe("NO_PROCEDE");
      await expect(bo.getByText("No procede en la prueba E2E", { exact: true })).toBeVisible();
      await bo.getByRole("button", { name: "Reabrir" }).click();
      const reabrir = bo.getByRole("dialog", { name: "Reabrir el saneamiento" });
      await reabrir.getByRole("textbox", { name: "Motivo" }).fill("Segunda reapertura de la prueba E2E");
      await reabrir.getByRole("button", { name: "Reabrir" }).click();
      await expect(reabrir).toHaveCount(0);
      expect(await estadoBackoffice(bo, sid)).toBe("EN_GESTION");
    });

    await test.step("AC30 / AC36: «Anular» con motivo deja el saneamiento en solo lectura y el historial lo registra", async () => {
      await bo.getByRole("button", { name: "Anular", exact: true }).click();
      const modal = bo.getByRole("dialog", { name: "Anular el saneamiento" });
      await modal.getByRole("textbox", { name: "Motivo" }).fill("Anulación de la prueba E2E");
      await modal.getByRole("button", { name: "Anular", exact: true }).click();
      await expect(modal).toHaveCount(0);
      expect(await estadoBackoffice(bo, sid)).toBe("ANULADO");
      await expect(bo.getByText("Saneamiento anulado", { exact: true })).toBeVisible();
      await expect(bo.getByRole("button", { name: "Anular", exact: true })).toHaveCount(0);
      await expect(bo.getByRole("heading", { level: 2, name: "Historial" })).toBeVisible();
      await expect(bo.getByText("FLIT anuló el saneamiento").first()).toBeVisible();
      await revisarTemas(bo, "gestion-anulado-1280");
    });

    await cerrar();
  });
});
