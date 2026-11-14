import { expect, test, type Page, type Route } from "@playwright/test";
import {
  APP,
  BACKOFFICE,
  CLIENTE,
  ancho,
  buscarEnBandeja,
  descargar,
  entrar,
  esperarTerminados,
  exigirCliente,
  fallarUnaVez,
  focoDentro,
  idVehiculo,
  leerGrilla,
  marcarPorApi,
  placaFicticia,
  prefijoFicticio,
  recorrerConTab,
  retener,
  revisarTemas,
  sembrarExpress,
  sembrarMasivo,
  sinDesbordeHorizontal,
  tomarPorApi,
} from "./saneamiento-comun";

/**
 * HU #14577 — saneamiento del lado de la compañía: PS-1 (tablero del lote) y PS-2 (panel «Deuda y saneamiento»).
 * Cubre AC5–AC13 y AC18 en el recorrido, y AC41/AC42 (axe en claro y oscuro, Tab a 360 px, sin desplazamiento horizontal)
 * con las capturas C-1 a C-6 de .claude/state/saneamiento/auditoria-ui.md.
 *
 * Necesita la suite local con simulación (scripts/dev/levantar-diagnostico-local.sh simulado). Los lotes se siembran en
 * cada corrida con placas ficticias (ver saneamiento-comun.ts). Variables:
 *   E2E_USUARIO / E2E_CLAVE — usuario de la compañía con diagnosticos.crear, diagnosticos.ver, reportes.descargar,
 *     saneamiento.solicitar y, para el aviso de «Crear trámite», derivar.
 *   E2E_BACKOFFICE_USUARIO / E2E_BACKOFFICE_CLAVE / E2E_EMPRESA — opcionales aquí: con ellas se toma un saneamiento como FLIT
 *     para ver la bandera en «Ver saneamiento» (AC7) y la etiqueta «En saneamiento».
 *
 * Lista congelada: nombres accesibles de los que depende este spec. Si uno cambia por decisión de UX, el spec se ajusta en
 * el mismo PR de la pantalla.
 *   - table «Semáforo de consultas del lote» · rowheader «<placa>»
 *   - button «Solicitar saneamiento de la placa <placa>» (aria-pressed, aria-disabled, descripción del motivo)
 *   - button «Ver saneamiento de la placa <placa>: En saneamiento»
 *   - button «Ver deuda de la placa <placa>» · button «Ver dictamen de <placa>» · button «Ver deuda y sanear»
 *   - group «Filtrar por saneamiento» · button «Con saneamiento» · button «Sin saneamiento»
 *   - searchbox «Buscar por placa o VIN» · button «Limpiar filtros» · texto «Ningún vehículo coincide»
 *   - checkbox «Seleccionar la placa <placa>» · button «Seleccionar todos los elegibles» · button «Quitar selección»
 *   - button «/^Sanear \(\d+\)$/» · button «/^Crear trámites \(\d+\)$/» · button «Crear trámite para la placa <placa>»
 *   - dialog «Deuda y saneamiento» · dialog «Dictamen · <placa>» · button «Cerrar»
 *   - link «Descargar PDF» · link «Descargar Excel» · table «Comparendos de SIMIT del vehículo» · columnheader «Valor»
 *   - button «/^SIMIT del propietario 1 de 2/» · button «/^SIMIT del propietario 2 de 2/»
 *   - texto «Saneamiento solicitado» · «Saneamiento en curso» · «Vehículo bloqueado» · «Faltan consultas por terminar»
 */

const ruta = (diagnosticoId: string, vehiculoId: string) => new RegExp(`/diagnosticos/${diagnosticoId}/vehiculos/${vehiculoId}/saneamiento$`);
const bandera = (page: Page, placa: string) => page.getByRole("button", { name: `Solicitar saneamiento de la placa ${placa}` });
const grilla = (page: Page) => page.getByRole("table", { name: /semáforo de consultas/i });
const fila = (page: Page, placa: string) => grilla(page).getByRole("row").filter({ has: page.getByRole("rowheader", { name: new RegExp(placa) }) });
const panelDeuda = (page: Page) => page.getByRole("dialog", { name: "Deuda y saneamiento" });

test.describe("PS-1 tablero del lote", () => {
  test("bandera en un clic con reversión, chips ?saneamiento=, «Sanear (N)» parcial y aviso en «Crear trámite»", async ({ page, browser }) => {
    exigirCliente();
    test.setTimeout(600_000);
    const prefijo = prefijoFicticio();
    // A: bandera y masivo con éxito · B: omitido por YA_SOLICITADO · C: solicitado en el masivo · D: lo toma FLIT · E: no termina.
    const [A, B, C, D, E] = [placaFicticia(prefijo, "1"), placaFicticia(prefijo, "2"), placaFicticia(prefijo, "1"), placaFicticia(prefijo, "3"), placaFicticia(prefijo, "7")];
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!);
    const { diagnosticoId: lote } = await sembrarMasivo(page, [A, B, C, D, E], { nombre: `E2E saneamiento PS-1 ${Date.now()}` });
    await esperarTerminados(page, lote, [A, B, C, D]);
    const [vidA, vidB, vidC, vidD] = await Promise.all([A, B, C, D].map((p) => idVehiculo(page, lote, p)));
    await page.reload();
    await expect(bandera(page, A)).toBeVisible();
    test.skip((await leerGrilla(page, lote)).vehiculos.every((v) => v.saneamiento === undefined), "El backend no expone `saneamiento` en la grilla (§2.6)");

    await test.step("AC5: marca en un clic, sin confirmación, y anuncia", async () => {
      await expect(bandera(page, A)).toHaveAttribute("aria-pressed", "false");
      await expect(bandera(page, A)).toHaveText("Sanear");
      const put = page.waitForResponse((r) => r.request().method() === "PUT" && ruta(lote, vidA).test(new URL(r.url()).pathname));
      await bandera(page, A).click();
      await expect(bandera(page, A)).toHaveAttribute("aria-pressed", "true");
      expect([200, 201]).toContain((await put).status());
      await expect(bandera(page, A)).toHaveText("Solicitado");
      await expect(bandera(page, A)).toBeFocused();
      await expect(fila(page, A)).toContainText("Saneamiento solicitado");
      await expect(page.getByText(`Saneamiento solicitado para ${A}.`)).toBeAttached();
      await expect(page.getByRole("dialog")).toHaveCount(0);
    });

    await test.step("AC6: desmarcar con 409 SANEAMIENTO_EN_GESTION revierte y explica", async () => {
      const fallo = await fallarUnaVez(page, "DELETE", ruta(lote, vidA), { status: 409, code: "SANEAMIENTO_EN_GESTION", error: "El saneamiento ya está en gestión." });
      await bandera(page, A).click();
      await expect(bandera(page, A)).toHaveAttribute("aria-pressed", "true");
      await expect(page.getByRole("alert").filter({ hasText: "FLIT ya empezó este saneamiento; ya no se puede quitar." })).toBeVisible();
      await expect(bandera(page, A)).toBeFocused();
      expect(fallo.usado()).toBe(true);
      await fallo.quitar();
    });

    await test.step("AC6: marcar con la red caída revierte y explica", async () => {
      const fallo = await fallarUnaVez(page, "PUT", ruta(lote, vidC), "red");
      await bandera(page, C).click();
      await expect(bandera(page, C)).toHaveAttribute("aria-pressed", "false");
      await expect(page.getByRole("alert").filter({ hasText: "No pudimos guardar el cambio. Intenta de nuevo." })).toBeVisible();
      await expect(fila(page, C)).not.toContainText("Saneamiento solicitado");
      await fallo.quitar();
    });

    await test.step("AC6: desmarcar de verdad", async () => {
      await bandera(page, A).click();
      await expect(bandera(page, A)).toHaveAttribute("aria-pressed", "false");
      await expect(fila(page, A)).not.toContainText("Saneamiento solicitado");
      await expect(page.getByText(`Se quitó la solicitud de saneamiento de ${A}.`)).toBeAttached();
    });

    await test.step("AC8: el vehículo que no termina tiene la bandera deshabilitada con el motivo", async () => {
      const e = bandera(page, E);
      await expect(e).toHaveAttribute("aria-disabled", "true");
      await expect(e).toHaveAccessibleDescription("Faltan consultas por terminar");
      await e.click();
      await expect(e).toHaveAttribute("aria-pressed", "false");
      const casilla = page.getByRole("checkbox", { name: `Seleccionar la placa ${E}` });
      if (await casilla.count()) {
        await expect(casilla).toBeDisabled();
        await expect(casilla).toHaveAccessibleDescription(/Faltan consultas por terminar/);
      }
    });

    await test.step("AC7: con el saneamiento tomado por FLIT la bandera es un indicador que abre PS-2", async () => {
      if (!BACKOFFICE.usuario || !BACKOFFICE.clave) {
        test.info().annotations.push({ type: "omitido", description: "AC7 sin E2E_BACKOFFICE_*: no se tomó el saneamiento de D" });
        return;
      }
      await marcarPorApi(page, lote, vidD);
      const contextoFlit = await browser.newContext();
      const bo = await contextoFlit.newPage();
      await entrar(bo, BACKOFFICE.usuario, BACKOFFICE.clave);
      const { id, empresa } = await buscarEnBandeja(bo, D);
      await tomarPorApi(bo, id, empresa);
      await contextoFlit.close();
      await page.reload();
      const ver = page.getByRole("button", { name: `Ver saneamiento de la placa ${D}: En saneamiento` });
      await expect(ver).toBeVisible();
      await expect(ver).not.toHaveAttribute("aria-pressed", /.*/);
      await expect(ver).toHaveAttribute("aria-haspopup", "dialog");
      await expect(fila(page, D)).toContainText("En saneamiento");
      await ver.click();
      await expect(panelDeuda(page)).toBeVisible();
      await page.keyboard.press("Escape");
      await expect(panelDeuda(page)).toHaveCount(0);
      await expect(ver).toBeFocused();
    });

    await test.step("C-1: bandera apagada, solicitada, no elegible y «Ver saneamiento» (1280 y 360)", async () => {
      await bandera(page, A).click();
      await expect(bandera(page, A)).toHaveAttribute("aria-pressed", "true");
      await revisarTemas(page, "tablero-1280", { captura: "C-1" });
      await ancho(page, 360);
      await revisarTemas(page, "tablero-360", { captura: "C-1" });
      await sinDesbordeHorizontal(page, "tablero-360");
      await recorrerConTab(page, "tablero-360", { pasos: 60 });
      await ancho(page, 1280);
    });

    await test.step("AC9 / C-2: chips «Con / Sin saneamiento» en la URL y vacío por filtro", async () => {
      const grupo = page.getByRole("group", { name: "Filtrar por saneamiento" });
      const con = grupo.getByRole("button", { name: "Con saneamiento" });
      const sin = grupo.getByRole("button", { name: "Sin saneamiento" });
      await con.click();
      await expect(con).toHaveAttribute("aria-pressed", "true");
      await expect(page).toHaveURL(/[?&]saneamiento=con(&|$)/);
      await expect(fila(page, A)).toBeVisible();
      await expect(fila(page, C)).toHaveCount(0);
      // Al cargar con el parámetro (lo usa la campana), el chip nace activo.
      await page.goto(`${APP}/diagnosticos/${lote}?saneamiento=con`);
      await expect(con).toHaveAttribute("aria-pressed", "true");
      await expect(fila(page, A)).toBeVisible();
      await revisarTemas(page, "tablero-chip-con-1280", { captura: "C-2" });
      await page.getByRole("searchbox", { name: "Buscar por placa o VIN" }).fill(C);
      await expect(page.getByText("Ningún vehículo coincide")).toBeVisible();
      await expect(page.getByText("Quita el filtro de saneamiento o prueba con otra búsqueda.")).toBeVisible();
      await revisarTemas(page, "tablero-vacio-filtro-1280", { captura: "C-2" });
      await ancho(page, 360);
      await revisarTemas(page, "tablero-vacio-filtro-360", { captura: "C-2" });
      await sinDesbordeHorizontal(page, "tablero-vacio-filtro-360");
      await ancho(page, 1280);
      await page.getByRole("button", { name: "Limpiar filtros" }).click();
      await expect(con).toHaveAttribute("aria-pressed", "false");
      await expect(page).not.toHaveURL(/saneamiento=/);
      await expect(page.getByRole("searchbox", { name: "Buscar por placa o VIN" })).toHaveValue("");
      await sin.click();
      await expect(sin).toHaveAttribute("aria-pressed", "true");
      await expect(page).toHaveURL(/[?&]saneamiento=sin(&|$)/);
      await expect(fila(page, A)).toHaveCount(0);
      await expect(fila(page, C)).toBeVisible();
      await sin.click();
      await expect(sin).toHaveAttribute("aria-pressed", "false");
      await expect(page).not.toHaveURL(/saneamiento=/);
    });

    await test.step("AC10 / AC11 / C-3: «Sanear (2)» con un omitido real por YA_SOLICITADO", async () => {
      // B se marca por otra vía mientras el sondeo de la grilla está retenido: la pantalla aún lo cree elegible y el
      // servidor responde de verdad con 1 solicitado y 1 omitido. El sondeo se suelta después.
      const sondeo = await retener(page, /\/grilla\?desde=/);
      await marcarPorApi(page, lote, vidB);
      await page.getByRole("checkbox", { name: `Seleccionar la placa ${B}` }).check();
      await page.getByRole("checkbox", { name: `Seleccionar la placa ${C}` }).check();
      const sanear = page.getByRole("button", { name: /^Sanear \(\d+\)$/ });
      await expect(sanear).toHaveText(/Sanear \(2\)/);
      const post = page.waitForResponse((r) => r.request().method() === "POST" && new URL(r.url()).pathname.endsWith("/saneamientos/solicitudes"));
      await sanear.click();
      const cuerpo = (await (await post).json()) as { solicitados: number; omitidos: { vehiculo_id: string; motivo: string }[] };
      expect(cuerpo.solicitados).toBe(1);
      expect(cuerpo.omitidos).toEqual([{ vehiculo_id: vidB, motivo: "YA_SOLICITADO" }]);
      const resultado = "Se solicitó el saneamiento de 1 vehículo. 1 se omitió: 1 que ya tenía saneamiento.";
      await expect(page.getByText(resultado)).toBeVisible();
      await expect(sanear).toBeFocused();
      await expect(fila(page, C)).toContainText("Saneamiento solicitado");
      await revisarTemas(page, "tablero-resultado-parcial-1280", { captura: "C-3" });
      await ancho(page, 360);
      await revisarTemas(page, "tablero-resultado-parcial-360", { captura: "C-3" });
      await ancho(page, 1280);
      await sondeo.soltar();
      await expect(fila(page, B)).toContainText("Saneamiento solicitado");
    });

    await test.step("C-3: con la selección vacía el foco va al aviso del resultado", async () => {
      const quitar = page.getByRole("button", { name: "Quitar selección" });
      if (await quitar.isVisible()) await quitar.click();
      // A volvió a quedar sin saneamiento en C-1: se desmarca para que sea elegible.
      if ((await bandera(page, A).getAttribute("aria-pressed")) === "true") {
        await bandera(page, A).click();
        await expect(bandera(page, A)).toHaveAttribute("aria-pressed", "false");
      }
      await page.getByRole("checkbox", { name: `Seleccionar la placa ${A}` }).check();
      await page.getByRole("button", { name: /^Sanear \(1\)$/ }).click();
      const texto = "Se solicitó el saneamiento de 1 vehículo.";
      await expect(page.locator(":focus")).toContainText(texto);
      await revisarTemas(page, "tablero-foco-aviso-1280", { captura: "C-3" });
    });

    await test.step("AC12 / C-4: aviso «Saneamiento en curso» al crear trámite, individual y masivo", async () => {
      const individual = page.getByRole("button", { name: `Crear trámite para la placa ${A}` });
      if (!(await individual.count())) {
        test.info().annotations.push({ type: "omitido", description: "AC12 sin la capacidad derivar o con la derivación apagada" });
        return;
      }
      await individual.click();
      const modal = page.getByRole("dialog");
      await expect(modal.getByText("Saneamiento en curso")).toBeVisible();
      await expect(modal.getByText("Este vehículo tiene un saneamiento en curso. Puedes crear el trámite igual.")).toBeVisible();
      await revisarTemas(page, "crear-tramite-individual-1280", { captura: "C-4", paginaCompleta: false });
      await page.keyboard.press("Escape");
      await expect(modal).toHaveCount(0);

      const casillaA = page.getByRole("checkbox", { name: `Seleccionar la placa ${A}` });
      const casillaC = page.getByRole("checkbox", { name: `Seleccionar la placa ${C}` });
      if ((await casillaA.isEnabled()) && (await casillaC.isEnabled())) {
        await casillaA.check();
        await casillaC.check();
        const crear = page.getByRole("button", { name: /^Crear trámites \(\d+\)$/ });
        if ((await crear.getAttribute("aria-disabled")) !== "true") {
          await crear.click();
          await expect(page.getByRole("dialog").getByText(/de los vehículos seleccionados tienen? un saneamiento en curso\. Puedes crear los trámites igual\./)).toBeVisible();
          await revisarTemas(page, "crear-tramite-masivo-1280", { captura: "C-4", paginaCompleta: false });
          await page.keyboard.press("Escape");
        }
      }
    });
  });
});

test.describe("PS-2 panel «Deuda y saneamiento»", () => {
  test("se abre desde la fila, desde el dictamen bloqueado y desde la grilla de placas; foco atrapado y devuelto; descargas", async ({ page }) => {
    exigirCliente();
    test.setTimeout(420_000);
    const F = placaFicticia(prefijoFicticio(), "2");
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!);
    const { diagnosticoId: lote, conAnalisis } = await sembrarExpress(page, F, { nombre: `E2E saneamiento PS-2 ${Date.now()}`, conAnalisis: true });
    await esperarTerminados(page, lote, [F]);
    const vid = await idVehiculo(page, lote, F);
    await page.reload();
    const verDeuda = page.getByRole("button", { name: `Ver deuda de la placa ${F}` });
    await expect(verDeuda).toBeVisible();

    await test.step("C-5: cargando (respuesta real retenida)", async () => {
      const deuda = await retener(page, new RegExp(`/vehiculos/${vid}/deuda$`));
      await verDeuda.click();
      await expect(panelDeuda(page).getByRole("status", { name: "Cargando deuda" })).toBeVisible();
      await revisarTemas(page, "panel-cargando-1280", { captura: "C-5", paginaCompleta: false });
      await deuda.soltar();
    });

    await test.step("AC13: desde la fila, con el foco dentro y atrapado", async () => {
      const panel = panelDeuda(page);
      await expect(panel).toBeVisible();
      await expect(panel).toContainText(F);
      expect(await focoDentro(panel)).toBe(true);
      for (let i = 0; i < 20; i++) await page.keyboard.press("Tab");
      expect(await focoDentro(panel)).toBe(true);
      for (let i = 0; i < 5; i++) await page.keyboard.press("Shift+Tab");
      expect(await focoDentro(panel)).toBe(true);
    });

    await test.step("AC15: deuda por fuente con el valor tal como llega y sin total", async () => {
      const panel = panelDeuda(page);
      const simit = panel.getByRole("button", { name: /^SIMIT del vehículo/ });
      await expect(simit).toHaveAttribute("aria-expanded", "true");
      await expect(simit).toContainText("Con deuda");
      const comparendos = panel.getByRole("table", { name: "Comparendos de SIMIT del vehículo" });
      await expect(comparendos.getByRole("columnheader", { name: "Valor" })).toBeVisible();
      // S17: el SIMIT no suma; la tabla de comparendos no tiene fila de total.
      await expect(comparendos.getByText(/total/i)).toHaveCount(0);
    });

    await test.step("AC14: la bandera de la cabecera marca y desmarca con la lógica de PS-1", async () => {
      const panel = panelDeuda(page);
      const b = panel.getByRole("button", { name: `Solicitar saneamiento de la placa ${F}` });
      await b.click();
      await expect(b).toHaveAttribute("aria-pressed", "true");
      await expect(panel.getByRole("heading", { name: "Saneamiento", exact: true })).toBeVisible();
      await expect(b).toBeFocused();
      await b.click();
      await expect(b).toHaveAttribute("aria-pressed", "false");
      await expect(panel.getByRole("heading", { name: "Saneamiento", exact: true })).toHaveCount(0);
    });

    await test.step("AC18: descargas del pie", async () => {
      const panel = panelDeuda(page);
      await descargar(page, panel.getByRole("link", { name: "Descargar PDF" }), /\.pdf$/i);
      await descargar(page, panel.getByRole("link", { name: "Descargar Excel" }), /\.xlsx$/i);
    });

    await test.step("C-5 / AC41 / AC42: panel lleno en 1280 (panel de 640) y 360; Tab dentro del panel", async () => {
      await revisarTemas(page, "panel-640", { captura: "C-5", paginaCompleta: false });
      await ancho(page, 360);
      await revisarTemas(page, "panel-360", { captura: "C-5", paginaCompleta: false });
      await sinDesbordeHorizontal(page, "panel-360");
      await recorrerConTab(page, "panel-360", { ambito: panelDeuda(page), pasos: 30 });
      await ancho(page, 1280);
    });

    await test.step("AC13: Escape cierra y el foco vuelve a «Ver deuda»", async () => {
      await page.keyboard.press("Escape");
      await expect(panelDeuda(page)).toHaveCount(0);
      await expect(verDeuda).toBeFocused();
    });

    await test.step("AC13: desde «Ver deuda y sanear» del dictamen bloqueado; el foco vuelve a «Ver dictamen»", async () => {
      const dictamen = page.getByRole("button", { name: `Ver dictamen de ${F}` });
      if (!conAnalisis || !(await dictamen.count())) {
        test.info().annotations.push({ type: "omitido", description: "Entrada desde el dictamen: la compañía no permite Con Análisis IA" });
        return;
      }
      await dictamen.click();
      const panelDictamen = page.getByRole("dialog", { name: `Dictamen · ${F}` });
      await expect(panelDictamen.getByText("Vehículo bloqueado")).toBeVisible();
      await panelDictamen.getByRole("button", { name: "Ver deuda y sanear" }).click();
      await expect(panelDictamen).toHaveCount(0);
      await expect(panelDeuda(page)).toBeVisible();
      await expect(panelDeuda(page).getByText(/^Bloquea:/).first()).toBeVisible();
      expect(await focoDentro(panelDeuda(page))).toBe(true);
      await panelDeuda(page).getByRole("button", { name: "Cerrar" }).click();
      await expect(panelDeuda(page)).toHaveCount(0);
      await expect(dictamen).toBeFocused();
    });

    await test.step("C-6 / UA-3: dos propietarios en SIMIT con nombres y plegado independientes", async () => {
      // Variación de la respuesta real dentro del contrato: dos entradas SIMIT_PROPIETARIO (BE-C-1, una por propietario).
      const rutaDeuda = new RegExp(`/vehiculos/${vid}/deuda$`);
      const dosPropietarios = async (route: Route) => {
        const real = await route.fetch();
        const cuerpo = (await real.json()) as { fuentes: Record<string, unknown>[] };
        const base = { fuente: "SIMIT_PROPIETARIO", estado: "SIN_HALLAZGO", consultado_en: new Date().toISOString(), paz_y_salvo: true, comparendos: [] };
        cuerpo.fuentes = [
          ...cuerpo.fuentes.filter((f) => f.fuente !== "SIMIT_PROPIETARIO"),
          { ...base, persona: { tipo_documento: "CC", documento: "1000000001" } },
          { ...base, persona: { tipo_documento: "CC", documento: "1000000002" } },
        ];
        await route.fulfill({ response: real, json: cuerpo });
      };
      await page.route(rutaDeuda, dosPropietarios);
      await verDeuda.click();
      const panel = panelDeuda(page);
      const uno = panel.getByRole("button", { name: /^SIMIT del propietario 1 de 2/ });
      const dos = panel.getByRole("button", { name: /^SIMIT del propietario 2 de 2/ });
      await expect(uno).toBeVisible();
      await expect(dos).toBeVisible();
      const antes = await dos.getAttribute("aria-expanded");
      await uno.click();
      await expect(dos).toHaveAttribute("aria-expanded", antes ?? "false");
      await revisarTemas(page, "panel-dos-propietarios-640", { captura: "C-6", paginaCompleta: false });
      await page.unroute(rutaDeuda, dosPropietarios);
      await page.keyboard.press("Escape");
    });

    await test.step("AC13: desde «Ver deuda» de la grilla de placas (PS-3)", async () => {
      await page.goto(`${APP}/?vista=vehiculos&placa=${encodeURIComponent(F)}`);
      const tabla = page.getByRole("table", { name: "Vehículos de la compañía" });
      const ver = tabla.getByRole("button", { name: `Ver deuda de la placa ${F}` });
      await ver.click();
      await expect(panelDeuda(page)).toBeVisible();
      expect(await focoDentro(panelDeuda(page))).toBe(true);
      await page.keyboard.press("Escape");
      await expect(panelDeuda(page)).toHaveCount(0);
      await expect(ver).toBeFocused();
    });
  });
});
