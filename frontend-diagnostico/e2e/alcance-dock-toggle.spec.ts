import { expect, test, type Locator, type Page } from "@playwright/test";
import { ADMIN, leerSesion, variarCapacidades } from "./ola-ux-comun";
import { APP, BACKOFFICE, CLIENTE, ancho, entrar, exigirBackoffice, exigirCliente, ponerTema, sinDesbordeHorizontal } from "./saneamiento-comun";
import {
  TODAS_LAS_FUENTES,
  capturar,
  colorDeToken,
  contraste,
  contratarFuentes,
  esperarDockEquilibrado,
  ladosDock,
  revisarTemasReq,
} from "./ola-requerimiento-comun";

/**
 * HU #14583 · Alcance (Express y Masivo), dock equilibrado y toggle marcado (oleada «requerimiento.pdf», 2026-11-16).
 * spec-ui §2 (A2.1–A2.8), §3 (A3.1–A3.4) y §9 (A9.1–A9.4).
 *
 * Modo de la API: suite local real con simulación. `page.route` solo para:
 *   - variar GET /configuracion dentro del contrato (fuentes contratadas de la compañía y `fuentes_disponibles`), para tener
 *     un grupo vacío, M distinto del catálogo y el estado «solo RUNT»;
 *   - variar las capacidades REALES de /sesion (`gestionarUsuarios`) mientras be2b no la publique (la API vuelve a exigir el
 *     permiso en /usuarios, que este spec no abre).
 * Variables: E2E_USUARIO / E2E_CLAVE (compañía, diagnosticos.crear); opcionales E2E_ADMIN_USUARIO / E2E_ADMIN_CLAVE (admin de
 * la compañía) y E2E_BACKOFFICE_USUARIO / E2E_BACKOFFICE_CLAVE / E2E_EMPRESA (SuperAdmin FLIT).
 *
 * Lista congelada (componentes de la Fase 2; ajustar en la Fase 3 si el nombre final cambia):
 *   - group «/^Alcance/» (fieldset; el contador puede ir dentro del legend) · heading «Alcance»
 *   - texto «N de M fuentes» con sr-only « seleccionadas» y aria-live=polite (en el nodo o un ancestro)
 *   - group «Por Vehículo» · «Por Propietario» · «Por Locatario» · checkbox «<fuente>» (dentro de un li por tarjeta)
 *   - radiogroup «Tipo de diagnóstico» · radio «Express» / «Masivo» (SelectorSegmentado)
 *   - navigation «Navegación principal» · link «Inicio Diagnóstico» (FAB) · link «Usuarios» (href /usuarios)
 * PENDIENTE de componente aún inexistente: el contador y `distribucion="equilibrada"` del Dock (fe1-kit).
 */
test.describe.configure({ mode: "serial" });

const SPEC = "alcance";

const MODOS = ["express", "masivo"] as const;
const alcance = (page: Page) => page.getByRole("region", { name: "Parámetros" }).getByRole("group", { name: /^Alcance(\s|$)/ }).first();
const grupo = (page: Page, nombre: string) => alcance(page).getByRole("group", { name: nombre, exact: true });
const contador = (page: Page) => alcance(page).getByText(/^\s*\d+\s+de\s+\d+\s+fuentes/);
/** Tarjeta de una fuente: el `li` que contiene su casilla. */
const tarjeta = (page: Page, fuente: string) => alcance(page).locator("li").filter({ has: page.getByRole("checkbox", { name: fuente, exact: true }) });

async function leerContador(page: Page): Promise<{ n: number; m: number }> {
  const t = ((await contador(page).first().textContent()) ?? "").replace(/\s+/g, " ");
  const r = t.match(/(\d+) de (\d+) fuentes/);
  expect(r, `contador «${t}»`).toBeTruthy();
  return { n: Number(r![1]), m: Number(r![2]) };
}

async function caja(l: Locator) {
  await expect(l).toBeVisible();
  return (await l.boundingBox())!;
}

async function irANuevo(page: Page, modo: (typeof MODOS)[number]) {
  await page.goto(`${APP}/diagnosticos/nuevo?modo=${modo}`);
  await expect(page.getByRole("radio", { name: modo === "express" ? "Express" : "Masivo" })).toHaveAttribute("aria-checked", "true");
  await expect(page.getByRole("checkbox", { name: "RUNT vehículo" })).toBeVisible();
}

test.describe("HU #14583 Alcance: contador, grupos en filas, mismo ancho y grupo vacío oculto", () => {
  test.beforeEach(async ({ page }) => {
    exigirCliente();
    await page.setViewportSize({ width: 1280, height: 900 });
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, "/diagnosticos");
  });

  for (const modo of MODOS) {
    test(`${modo}: «N de M» cuenta las fuentes de la compañía, va junto al título y se anuncia`, async ({ page }) => {
      // Variación de /configuracion: 5 contratadas (3 de Vehículo, 2 de Propietario, ninguna de Locatario), todas disponibles.
      const contratadas = ["RUNT", "SIMIT_VEHICULO", "AVALUO", "RUNT_PROPIETARIO", "SIMIT_PROPIETARIO"];
      await contratarFuentes(page, contratadas);
      await irANuevo(page, modo);

      // A2.1: M = ofrecidas (5), no el catálogo (8).
      const { n, m } = await leerContador(page);
      expect(m, "M = fuentes ofrecidas a la compañía").toBe(contratadas.length);
      expect(n).toBeGreaterThanOrEqual(1);
      expect(n).toBeLessThanOrEqual(m);

      // A2.1: misma línea que el título «Alcance».
      const [t, c] = [await caja(alcance(page).getByRole("heading", { name: "Alcance", exact: true })), await caja(contador(page).first())];
      expect(Math.abs(t.y + t.height / 2 - (c.y + c.height / 2)), "contador en la línea del título").toBeLessThanOrEqual(12);
      expect(c.x, "contador a la derecha del título").toBeGreaterThan(t.x);

      // A2.2: aria-live=polite y texto SR «… seleccionadas».
      const vivo = await contador(page).first().evaluate((el) => el.closest("[aria-live]")?.getAttribute("aria-live") ?? null);
      expect(vivo).toBe("polite");
      await expect(contador(page).first()).toContainText(/seleccionadas/);
      await capturar(page, SPEC, 1, `${modo}-contador-n-de-m`);

      // Al marcar o desmarcar una fuente libre, N cambia en 1 y M no.
      const libre = page.getByRole("checkbox", { name: "Avalúo comercial", exact: true });
      const antes = await libre.isChecked();
      await libre.click();
      const despues = await leerContador(page);
      expect(despues.m).toBe(m);
      expect(despues.n, "N sigue a la casilla").toBe(antes ? n - 1 : n + 1);
      await capturar(page, SPEC, 2, `${modo}-contador-tras-marcar`);
      await libre.click();
    });

    test(`${modo}: grupos en filas, fuentes en horizontal con el mismo ancho y «Por Locatario» oculto sin fuentes`, async ({ page }) => {
      await contratarFuentes(page, ["RUNT", "SIMIT_VEHICULO", "AVALUO", "IMPUESTO_DEPARTAMENTAL", "RUNT_PROPIETARIO", "SIMIT_PROPIETARIO"]);
      await irANuevo(page, modo);

      // A2.4: el grupo sin fuentes no existe y el texto viejo no aparece.
      await expect(grupo(page, "Por Vehículo")).toBeVisible();
      await expect(grupo(page, "Por Propietario")).toBeVisible();
      await expect(grupo(page, "Por Locatario")).toHaveCount(0);
      await expect(page.getByText(/Tu contrato no incluye fuentes de este grupo/)).toHaveCount(0);

      // Grupos apilados: Propietario debajo de Vehículo.
      const [gv, gp] = [await caja(grupo(page, "Por Vehículo")), await caja(grupo(page, "Por Propietario"))];
      expect(gp.y, "Por Propietario en otra fila").toBeGreaterThanOrEqual(gv.y + gv.height - 1);

      // Fuentes del grupo Vehículo en una sola fila (4 columnas a ≥ 40rem de contenedor).
      const vehiculo = ["RUNT vehículo", "SIMIT vehículo", "Avalúo comercial", "Impuesto departamental"];
      const cajasV = await Promise.all(vehiculo.map((f) => caja(tarjeta(page, f))));
      for (const k of cajasV) expect(Math.abs(k.y - cajasV[0].y), "fuentes de Vehículo en horizontal").toBeLessThanOrEqual(2);

      // A2.3: mismo ancho (±1 px) entre una tarjeta del grupo de 4 y una del grupo de 2.
      const [rv, rp] = [await caja(tarjeta(page, "RUNT vehículo")), await caja(tarjeta(page, "RUNT propietario"))];
      expect(Math.abs(rv.width - rp.width), "tarjetas del mismo ancho entre grupos").toBeLessThanOrEqual(1);
      expect(Math.abs(rv.x - rp.x), "alineadas en la misma rejilla").toBeLessThanOrEqual(1);

      // A2.5: «Dependiente» a 12 px y en la descripción de la casilla.
      const dependiente = tarjeta(page, "RUNT propietario").getByText("Dependiente", { exact: true });
      await expect(dependiente).toBeVisible();
      expect(await dependiente.evaluate((el) => parseFloat(getComputedStyle(el).fontSize))).toBeGreaterThanOrEqual(12);
      await expect(page.getByRole("checkbox", { name: "RUNT propietario", exact: true })).toHaveAccessibleDescription(/Dependiente/);

      await capturar(page, SPEC, 3, `${modo}-grupos-en-filas-mismo-ancho`);
      await revisarTemasReq(page, `alcance-${modo}-1280`, { captura: "AL-1" });

      // A2.8 / §2.6: 320 px sin scroll horizontal, una columna.
      await page.setViewportSize({ width: 320, height: 800 });
      await page.waitForTimeout(200);
      await sinDesbordeHorizontal(page, `alcance-${modo}-320`);
      const [a, b] = [await caja(tarjeta(page, "RUNT vehículo")), await caja(tarjeta(page, "SIMIT vehículo"))];
      expect(b.y, "320: una columna").toBeGreaterThanOrEqual(a.y + a.height - 1);
      await capturar(page, SPEC, 4, `${modo}-320-una-columna`, { paginaCompleta: true });
      await revisarTemasReq(page, `alcance-${modo}-320`, { captura: "AL-1" });
    });
  }

  test("express: con todas las fuentes, «Por Locatario» con el texto nuevo de dependencia (punto 3)", async ({ page }) => {
    await contratarFuentes(page, TODAS_LAS_FUENTES);
    await irANuevo(page, "express");
    expect((await leerContador(page)).m).toBe(8);
    await expect(grupo(page, "Por Locatario")).toBeVisible();
    // A2.6 / spec-ui §8: el locatario ya sale del RUNT, no del documento declarado.
    await expect(alcance(page).getByText("Depende del locatario que reporta el RUNT vehículo.", { exact: true })).toBeVisible();
    await expect(page.getByText(/documento del locatario que declara el cliente/i)).toHaveCount(0);
    await capturar(page, SPEC, 5, "express-locatario-dependencia-runt");
  });

  test("masivo: solo RUNT contratado → «1 de 1 fuentes» y solo el grupo Vehículo (estado vacío de §2.5)", async ({ page }) => {
    await contratarFuentes(page, ["RUNT"]);
    await irANuevo(page, "masivo");
    expect(await leerContador(page)).toEqual({ n: 1, m: 1 });
    await expect(grupo(page, "Por Vehículo")).toBeVisible();
    await expect(grupo(page, "Por Propietario")).toHaveCount(0);
    await expect(grupo(page, "Por Locatario")).toHaveCount(0);
    // RUNT bloqueada, enfocable y con el motivo (Tooltip nuevo).
    const runt = page.getByRole("checkbox", { name: "RUNT vehículo", exact: true });
    await expect(runt).toHaveAttribute("aria-disabled", "true");
    await expect(runt).toHaveAccessibleDescription(/siempre se consulta/);
    await capturar(page, SPEC, 6, "masivo-solo-runt-1-de-1");
  });
});

test.describe("HU #14583 toggle marcado (SelectorSegmentado) tinta + blanco, AA en claro y oscuro", () => {
  test("«Tipo de diagnóstico»: la opción marcada usa --kit-tinta y texto blanco; en oscuro lleva aro; contraste ≥ 4.5", async ({ page }) => {
    exigirCliente();
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, "/diagnosticos");
    await page.goto(`${APP}/diagnosticos/nuevo?modo=express`);
    const grupoModo = page.getByRole("radiogroup", { name: "Tipo de diagnóstico" });
    await expect(grupoModo).toBeVisible();

    for (const tema of ["claro", "oscuro"] as const) {
      await ponerTema(page, tema);
      await page.waitForTimeout(250);
      const marcada = grupoModo.getByRole("radio", { checked: true });
      const libre = grupoModo.getByRole("radio", { checked: false }).first();
      const [fondo, texto, sombra] = await marcada.evaluate((el) => {
        const s = getComputedStyle(el);
        return [s.backgroundColor, s.color, s.boxShadow];
      });
      // A3.1: tinta (#162744) por token y texto --di-on-accent (blanco).
      expect.soft(fondo, `${tema}: fondo de la marcada = --kit-tinta`).toBe(await colorDeToken(marcada, "--kit-tinta"));
      expect.soft(texto, `${tema}: texto blanco`).toBe("rgb(255, 255, 255)");
      expect.soft(contraste(texto, fondo), `${tema}: contraste texto/fondo`).toBeGreaterThanOrEqual(4.5);
      // A3.3: la no marcada no lleva la tinta.
      expect.soft(await libre.evaluate((el) => getComputedStyle(el).backgroundColor), `${tema}: la no marcada sin tinta`).not.toBe(fondo);
      // A3.2: en oscuro el aro interior --di-heading marca el estado (1.4.11).
      if (tema === "oscuro") expect.soft(sombra, "oscuro: aro interior").toMatch(/inset/);
      await capturar(page, SPEC, tema === "claro" ? 7 : 8, `toggle-marcado-${tema}`);
    }
    await ponerTema(page, "claro");

    // A3.4: el mismo aspecto en otro toggle del producto (Express/Masivo es el que siempre existe; «Con Análisis IA» si aplica).
    await revisarTemasReq(page, "toggle-nuevo-1280", { captura: "TG-1", paginaCompleta: false });
  });
});

test.describe("HU #14583 dock equilibrado (2|2 y 3|2) con «Usuarios» solo para quien gestiona usuarios", () => {
  test("usuario de la compañía: reparto ceil/floor, sin espaciadores y sin «Usuarios»", async ({ page }) => {
    exigirCliente();
    await page.setViewportSize({ width: 1280, height: 800 });
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, "/diagnosticos");
    const sesion = await leerSesion(page);
    test.skip(Boolean(sesion.capacidades.gestionarUsuarios), "E2E_USUARIO gestiona usuarios: no sirve para este caso");
    const lados = await ladosDock(page);
    esperarDockEquilibrado(lados, "usuario");
    expect([...lados.izquierda, ...lados.derecha]).not.toContain("Usuarios");
    await expect(page.getByRole("link", { name: "Usuarios", exact: true })).toHaveCount(0);
    await capturar(page, SPEC, 9, "dock-usuario-equilibrado");
    await revisarTemasReq(page, "dock-usuario-1280", { captura: "DK-1", paginaCompleta: false });
  });

  test("admin de la compañía: Diagnósticos · Reportes | Usuarios · Configuración (2|2)", async ({ page }) => {
    test.skip(!ADMIN.usuario || !ADMIN.clave, "Faltan E2E_ADMIN_USUARIO / E2E_ADMIN_CLAVE");
    await page.setViewportSize({ width: 1280, height: 800 });
    await entrar(page, ADMIN.usuario!, ADMIN.clave!, "/diagnosticos");
    // Mientras be2b no publique `gestionarUsuarios`, se fuerza en la respuesta real de /sesion (solo lo que pinta el front).
    if (!(await leerSesion(page)).capacidades.gestionarUsuarios) await variarCapacidades(page, { gestionarUsuarios: true });
    await page.reload();
    const lados = await ladosDock(page);
    esperarDockEquilibrado(lados, "admin");
    const todas = [...lados.izquierda, ...lados.derecha];
    if (todas.length === 4) {
      expect(lados.izquierda).toEqual(["Diagnósticos", "Reportes"]);
      expect(lados.derecha).toEqual(["Usuarios", "Configuración"]);
    } else {
      test.info().annotations.push({ type: "perfil", description: `El admin de prueba ve ${todas.join(" · ")} (sin las 4 de spec-ui §9)` });
    }
    await expect(page.getByRole("link", { name: "Usuarios", exact: true }).first()).toHaveAttribute("href", /\/usuarios$/);
    await capturar(page, SPEC, 10, "dock-admin-2-2");
    await revisarTemasReq(page, "dock-admin-1280", { captura: "DK-2", paginaCompleta: false });
  });

  test("SuperAdmin: Diagnósticos · Reportes · Backoffice | Usuarios · Configuración (3|2)", async ({ page }) => {
    exigirBackoffice();
    await page.setViewportSize({ width: 1280, height: 800 });
    await entrar(page, BACKOFFICE.usuario!, BACKOFFICE.clave!, "/diagnosticos");
    if (!(await leerSesion(page)).capacidades.gestionarUsuarios) await variarCapacidades(page, { gestionarUsuarios: true });
    await page.reload();
    const lados = await ladosDock(page);
    esperarDockEquilibrado(lados, "superadmin");
    if ([...lados.izquierda, ...lados.derecha].length === 5) {
      expect(lados.izquierda).toEqual(["Diagnósticos", "Reportes", "Backoffice"]);
      expect(lados.derecha).toEqual(["Usuarios", "Configuración"]);
    }
    // El dock mide su contenido: no ocupa el ancho de la ventana.
    const nav = await page.getByRole("navigation", { name: "Navegación principal" }).first().boundingBox();
    expect(nav!.width, "cápsula ajustada al contenido").toBeLessThan(1280 - 48);
    await capturar(page, SPEC, 11, "dock-superadmin-3-2");
    await revisarTemasReq(page, "dock-superadmin-1280", { captura: "DK-3", paginaCompleta: false });

    // Móvil: el dock de escritorio se oculta y la entrada sigue en la hoja (DockMobile no se reparte).
    await ancho(page, 360);
    await sinDesbordeHorizontal(page, "dock-superadmin-360");
    await ancho(page, 1280);
  });
});
