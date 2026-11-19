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
  focoDentro,
  placaFicticia,
  prefijoFicticio,
  recorrerConTab,
  sembrarExpress,
} from "./saneamiento-comun";
import {
  abrirAcciones,
  abrirDictamenDesdeAcciones,
  anchoPanel,
  celdaDictamen,
  clicEnVelo,
  disparadorAcciones,
  itemAuditoria,
  itemDictamen,
  maxDialogos,
  panelDe,
  revisarTemasOla,
  vigilarDialogos,
} from "./ola-ux-comun";

/**
 * FE-1 · Paneles laterales de Diagnóstico como modal de 640 px (spec-ui §1; decisiones U6 y V1 = `PanelModal`).
 *
 * Gherkin (particion-hus.md FE-1): «tamaño panel aditivo», «mismo contenido en modal», «D-15 y foco», «no perder lo escrito».
 * «Detalle de celda dentro del modal» se cubre en Vitest (DetalleCelda.test.tsx), no aquí.
 *
 * Modo de la API: suite local real con simulación. Siembra un Express con una placa ficticia terminada en 2 (SIMIT en rojo;
 * con Análisis IA el dictamen bloquea por SIMIT_VEHICULO). `page.route` no se usa.
 * Variables: E2E_USUARIO / E2E_CLAVE (compañía, con saneamiento.solicitar y diagnosticos.ver) y, para la auditoría,
 * E2E_BACKOFFICE_USUARIO / E2E_BACKOFFICE_CLAVE / E2E_EMPRESA (SuperAdmin FLIT y el uuid de la compañía de E2E_USUARIO).
 *
 * Lista congelada (ajustar en la Fase 3 si el componente final cambia un nombre):
 *   - button «Acciones de la placa <placa>» · menu · menuitem «/^Ver deuda/» (FE-2, aún no existe)
 *   - dialog «Deuda y saneamiento» · dialog «Dictamen · <placa>» · dialog «Auditoría del dictamen · <placa>»
 *   - heading (h2) con el título del modal · button «Cerrar» · button «Ver deuda y sanear»
 *   - button «Ver deuda de la placa <placa>» (Vehículos)
 *   Oleada 2026-11-16 (spec-ui §6.4): «Ver dictamen de <placa>» y «Auditoría del dictamen de <placa>» dejan de ser botones
 *   de la columna Placa y pasan a menuitem del menú «Acciones de la placa <placa>» (cliente y backoffice); el foco vuelve a
 *   ese disparador. La columna «Dictamen» queda como dato (td[data-columna=dictamen]).
 *   - table «/semáforo de consultas/i» · table «Vehículos de la compañía»
 * Mide el panel como el primer hijo del overlay `role=dialog` (Modal.tsx): 640 px ± 2 en 1280; viewport − 24 en 360.
 */
test.describe.configure({ mode: "serial" });

const ANCHO_PANEL = 640;
const TOLERANCIA = 2;
const dialogoDeuda = (page: Page) => page.getByRole("dialog", { name: "Deuda y saneamiento" });
const dialogoDictamen = (page: Page, placa: string) => page.getByRole("dialog", { name: `Dictamen · ${placa}` });

async function esperarAncho(dialogo: Locator, esperado: number) {
  await expect.poll(() => anchoPanel(dialogo), { message: `ancho del panel ≈ ${esperado} px` }).toBeGreaterThanOrEqual(esperado - TOLERANCIA);
  expect(await anchoPanel(dialogo)).toBeLessThanOrEqual(esperado + TOLERANCIA);
}

/** Lo que el modal conserva del panel (spec-ui §1.5): título h2 a la izquierda, «Cerrar» en la cabecera, un solo scroll. */
async function esperarCabeceraDePanel(dialogo: Locator, titulo: string | RegExp) {
  await expect(dialogo.getByRole("heading", { level: 2, name: titulo })).toBeVisible();
  await expect(dialogo.getByRole("button", { name: "Cerrar" })).toBeVisible();
  // El panel nunca es la hoja a pantalla completa del drawer: queda centrado dentro del velo.
  const panel = await panelDe(dialogo).boundingBox();
  const vista = dialogo.page().viewportSize()!;
  expect(panel!.x).toBeGreaterThan(0);
  expect(Math.abs(panel!.x + panel!.width / 2 - vista.width / 2)).toBeLessThanOrEqual(TOLERANCIA + 1);
}

test.describe("FE-1 paneles como modal de 640 px", () => {
  let placa = "";
  let lote = "";
  let conAnalisis = false;

  test("Deuda y saneamiento: 640 px, mismo contenido, el velo no cierra, Escape y «Cerrar» devuelven el foco", async ({ page }) => {
    exigirCliente();
    test.setTimeout(300_000);
    placa = placaFicticia(prefijoFicticio(), "2");
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, "/diagnosticos");
    ({ diagnosticoId: lote, conAnalisis } = await sembrarExpress(page, placa, { nombre: `E2E modal ${Date.now()}`, conAnalisis: true }));
    await esperarTerminados(page, lote, [placa]);
    await page.reload();
    await expect(page.getByRole("table", { name: /semáforo de consultas/i })).toBeVisible();

    await test.step("desde el menú «Acciones» de la fila: role=dialog de 640 px con la cabecera del panel", async () => {
      await vigilarDialogos(page);
      const { disparador, menu } = await abrirAcciones(page, placa);
      await menu.getByRole("menuitem", { name: /^Ver deuda/ }).click();
      const dialogo = dialogoDeuda(page);
      await expect(dialogo).toBeVisible();
      await expect(dialogo).toHaveAttribute("aria-modal", "true");
      await esperarAncho(dialogo, ANCHO_PANEL);
      await esperarCabeceraDePanel(dialogo, "Deuda y saneamiento");
      // Mismo contenido: la descripción es la placa y el cuerpo trae la deuda de SIMIT del vehículo.
      await expect(dialogo).toContainText(placa);
      await expect(dialogo.getByRole("button", { name: /^SIMIT del vehículo/ })).toBeVisible();
      expect(await maxDialogos(page)).toBe(1);
      expect(await focoDentro(dialogo)).toBe(true);

      // «No perder lo escrito» (diseno §d): Deuda y saneamiento tiene campos editables, el velo no la cierra.
      await clicEnVelo(page);
      await expect(dialogo).toBeVisible();

      // Trampa de foco: 20 Tab no salen del modal.
      for (let i = 0; i < 20; i++) await page.keyboard.press("Tab");
      expect(await focoDentro(dialogo)).toBe(true);

      await revisarTemasOla(page, "deuda-modal-1280", { captura: "M-1", paginaCompleta: false });

      await page.keyboard.press("Escape");
      await expect(page.getByRole("dialog")).toHaveCount(0);
      await expect(disparador).toBeFocused();
    });

    await test.step("«Cerrar» de la cabecera cierra y devuelve el foco al disparador de la fila", async () => {
      const { disparador, menu } = await abrirAcciones(page, placa);
      await menu.getByRole("menuitem", { name: /^Ver deuda/ }).click();
      await expect(dialogoDeuda(page)).toBeVisible();
      await dialogoDeuda(page).getByRole("button", { name: "Cerrar" }).click();
      await expect(page.getByRole("dialog")).toHaveCount(0);
      await expect(disparador).toBeFocused();
    });

    await test.step("360 px: el modal mide viewport − 24 y no desborda", async () => {
      await ancho(page, 360);
      const { menu } = await abrirAcciones(page, placa);
      await menu.getByRole("menuitem", { name: /^Ver deuda/ }).click();
      const dialogo = dialogoDeuda(page);
      await expect(dialogo).toBeVisible();
      await esperarAncho(dialogo, 360 - 24);
      const caja = await panelDe(dialogo).boundingBox();
      expect(caja!.y + caja!.height).toBeLessThanOrEqual(800);
      await recorrerConTab(page, "deuda-modal-360", { pasos: 40, ambito: dialogo });
      await revisarTemasOla(page, "deuda-modal-360", { captura: "M-1", paginaCompleta: false });
      await page.keyboard.press("Escape");
      await ancho(page, 1280);
    });
  });

  test("Dictamen: 640 px, foco al título, el velo sí cierra, Escape devuelve el foco a «Acciones»", async ({ page }) => {
    exigirCliente();
    test.skip(!lote, "Depende de la siembra del test anterior");
    test.setTimeout(240_000);
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, `/diagnosticos/${lote}`);
    await page.goto(`${APP}/diagnosticos/${lote}`);
    const dictamen = disparadorAcciones(page, placa);
    await expect(dictamen).toBeVisible();

    const { dialogo } = await abrirDictamenDesdeAcciones(page, placa);
    await esperarAncho(dialogo, ANCHO_PANEL);
    await esperarCabeceraDePanel(dialogo, `Dictamen · ${placa}`);
    // spec-ui §1.6: en Dictamen el foco inicial va al título (h2 tabindex=-1).
    await expect(dialogo.getByRole("heading", { level: 2, name: `Dictamen · ${placa}` })).toBeFocused();
    await revisarTemasOla(page, "dictamen-modal-1280", { captura: "M-2", paginaCompleta: false });

    // Sin override editable (compañía), el clic en el velo cierra como el panel.
    await clicEnVelo(page);
    await expect(page.getByRole("dialog")).toHaveCount(0);

    await abrirDictamenDesdeAcciones(page, placa);
    await page.keyboard.press("Escape");
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await expect(dictamen).toBeFocused();
  });

  test("D-15: de Dictamen a Deuda nunca hay dos role=dialog y al cerrar Deuda el foco vuelve a «Acciones»", async ({ page }) => {
    exigirCliente();
    test.skip(!lote, "Depende de la siembra del primer test");
    test.skip(!conAnalisis, "La compañía no permite Con Análisis IA: sin dictamen bloqueado no hay «Ver deuda y sanear»");
    test.setTimeout(240_000);
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, `/diagnosticos/${lote}`);
    await page.goto(`${APP}/diagnosticos/${lote}`);
    const dictamen = disparadorAcciones(page, placa);
    // El dictamen IA se calcula después de las consultas: se recarga hasta que la columna Dictamen (dato) diga «Bloqueado».
    await expect(async () => {
      await page.reload();
      await expect(celdaDictamen(page, placa)).toContainText(/Bloqueado/i, { timeout: 5_000 });
    }).toPass({ timeout: 150_000, intervals: [3_000] });

    await abrirDictamenDesdeAcciones(page, placa);
    const panelDictamen = dialogoDictamen(page, placa);
    await expect(panelDictamen.getByText("Vehículo bloqueado")).toBeVisible();
    await vigilarDialogos(page);
    await panelDictamen.getByRole("button", { name: "Ver deuda y sanear" }).click();
    await expect(dialogoDeuda(page)).toBeVisible();
    await expect(panelDictamen).toHaveCount(0);
    expect(await maxDialogos(page), "dialogs montados a la vez durante Dictamen → Deuda").toBe(1);
    await esperarAncho(dialogoDeuda(page), ANCHO_PANEL);
    await dialogoDeuda(page).getByRole("button", { name: "Cerrar" }).click();
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await expect(dictamen).toBeFocused();
  });

  test("Vehículos (/diagnosticos?vista=vehiculos): «Ver deuda» abre el mismo modal de 640 px", async ({ page }) => {
    exigirCliente();
    test.skip(!placa, "Depende de la siembra del primer test");
    test.setTimeout(180_000);
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, "/diagnosticos");
    await page.goto(`${APP}/diagnosticos?vista=vehiculos&placa=${encodeURIComponent(placa.slice(0, 3))}`);
    const ver = page.getByRole("table", { name: "Vehículos de la compañía" }).getByRole("button", { name: `Ver deuda de la placa ${placa}` });
    await ver.click();
    const dialogo = dialogoDeuda(page);
    await expect(dialogo).toBeVisible();
    await esperarAncho(dialogo, ANCHO_PANEL);
    await esperarCabeceraDePanel(dialogo, "Deuda y saneamiento");
    await expect(dialogo).toContainText(placa);
    await revisarTemasOla(page, "deuda-modal-vehiculos-1280", { captura: "M-3", paginaCompleta: false });
    await page.keyboard.press("Escape");
    await expect(dialogo).toHaveCount(0);
    await expect(ver).toBeFocused();
  });

  test("Backoffice: «Auditoría del dictamen» abre modal de 640 px y Escape devuelve el foco", async ({ page }) => {
    exigirBackoffice();
    test.skip(!lote, "Depende de la siembra del primer test (lote de la compañía E2E_EMPRESA)");
    test.setTimeout(180_000);
    await entrar(page, BACKOFFICE.usuario!, BACKOFFICE.clave!, "/");
    await page.goto(`${APP}/backoffice/diagnosticos/${lote}?empresa=${encodeURIComponent(EMPRESA!)}`);
    await expect(page.getByRole("table", { name: /semáforo de consultas/i })).toBeVisible();
    // Oleada 2026-11-16: la auditoría es un ítem del menú «Acciones» de la nueva columna del backoffice.
    const auditoria = disparadorAcciones(page, placa);
    await abrirAcciones(page, placa);
    const item = itemAuditoria(page, placa);
    test.skip((await item.count()) === 0, "El usuario de backoffice no tiene verAuditoria");
    await expect(itemDictamen(page, placa)).toBeVisible();
    await item.click();
    const dialogo = page.getByRole("dialog", { name: `Auditoría del dictamen · ${placa}` });
    await expect(dialogo).toBeVisible();
    await esperarAncho(dialogo, ANCHO_PANEL);
    await esperarCabeceraDePanel(dialogo, `Auditoría del dictamen · ${placa}`);
    await revisarTemasOla(page, "auditoria-modal-1280", { captura: "M-4", paginaCompleta: false });
    await page.keyboard.press("Escape");
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await expect(auditoria).toBeFocused();
  });
});
