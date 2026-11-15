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
  recorrerConTab,
  retener,
  sembrarExpress,
  sinDesbordeHorizontal,
} from "./saneamiento-comun";
import {
  RUTA,
  diasEntre,
  estadisticasVacias,
  hoyBogota,
  leerSesion,
  registrarPeticiones,
  responder,
  revisarTemasOla,
  sumarDias,
  tieneVistaGlobal,
} from "./ola-ux-comun";

/**
 * FE-4 (raíz, dock, redirecciones) y FE-5 (Dashboard de estadísticas por rol con selector de compañía). spec-ui §4 y §6;
 * contrato-api.md §1 (E1/E2/E3) y §6 (selector); decisiones U3, U7 y V4.
 *
 * Gherkin (particion-hus.md): FE-4 «raíz y dock», «enlaces viejos»; FE-5 «vista de compañía», «vista global y selector»,
 * «filtros de fecha», «estados», «accesibilidad».
 *
 * Modo de la API: suite local real. El test «lleno» siembra un Express (placa ficticia) para que el periodo tenga al menos un
 * diagnóstico. `page.route` solo para forzar estados que la suite no da sola: cargando (respuesta real retenida), vacío
 * (`EstadisticasDto` del contrato con ceros), error 500, 403 y 503 de Identidad en el selector.
 * Variables: E2E_USUARIO / E2E_CLAVE (compañía, sin vista global) y E2E_BACKOFFICE_USUARIO / E2E_BACKOFFICE_CLAVE /
 * E2E_EMPRESA (SuperAdmin FLIT con verEstadisticasGlobales; uuid de una compañía con Diagnóstico activo).
 * Al correr este archivo con otros de la ola: `--workers=1` (sesión única por usuario).
 *
 * Lista congelada (pantalla nueva de la Fase 2; ajustar en la Fase 3 si el nombre final cambia):
 *   - heading (h1) «Dashboard» · texto «/^Resumen de diagnósticos de /» · texto «Vista global · todas las compañías»
 *   - etiqueta «Desde» · etiqueta «Hasta» · texto «El rango no puede superar un año» · combobox «Compañía» (SearchableSelect)
 *     con «Todas las compañías» · option «<razón social>» · texto «No se pudo cargar la lista de compañías»
 *   - link o button «Nuevo diagnóstico» · heading (h2) «Uso en el tiempo», «Resultado», «Salud de las fuentes»,
 *     «Saneamientos», «Trámites», «Compañías con más uso» · figure (con figcaption) · table (datos alternativos)
 *   - texto «Aún no hay diagnósticos en este periodo» · button o link «Ver los últimos 90 días»
 *   - texto «/^No se pudo cargar/» · button «Reintentar» · texto «Cargando…» (role=status, aria-busy)
 *   - dock: link «Diagnósticos» (/diagnosticos) · link «Reportes» (/reportes) · link «Inicio Diagnóstico» (/)
 *   - heading (h1) «Diagnóstico vehicular» (el listado, ahora en /diagnosticos)
 */
test.describe.configure({ mode: "serial" });

const H1 = (page: Page) => page.getByRole("heading", { level: 1, name: "Dashboard" });
const desde = (page: Page) => page.getByLabel("Desde", { exact: true });
const hasta = (page: Page) => page.getByLabel("Hasta", { exact: true });
const selectorCompania = (page: Page) => page.getByRole("combobox", { name: "Compañía" });
const SECCIONES = ["Uso en el tiempo", "Resultado", "Salud de las fuentes", "Saneamientos", "Trámites"] as const;

/** Abre `/` (o `/?query`) y devuelve la primera respuesta de estadísticas que pide la vista. */
async function abrirDashboard(page: Page, ruta: RegExp, query = "") {
  const respuesta = page.waitForResponse((r) => r.request().method() === "GET" && ruta.test(r.url()));
  await page.goto(`${APP}/${query}`);
  await expect(H1(page)).toBeVisible();
  return respuesta;
}

test.describe("FE-4 raíz, dock y redirecciones", () => {
  test("«/» abre el Dashboard; el dock lleva a «Diagnósticos» y «Reportes»; /diagnosticos es el inicio de siempre", async ({ page }) => {
    exigirCliente();
    test.setTimeout(180_000);
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, "/");
    await expect(page).toHaveURL(`${APP}/`);
    await expect(H1(page)).toBeVisible();
    const sesion = await leerSesion(page);

    const diagnosticos = page.getByRole("link", { name: "Diagnósticos", exact: true }).first();
    await expect(diagnosticos).toHaveAttribute("href", "/diagnosticos");
    const reportes = page.getByRole("link", { name: "Reportes", exact: true });
    if (sesion.capacidades.verReportes || sesion.capacidades.backoffice) await expect(reportes.first()).toHaveAttribute("href", "/reportes");
    else await expect(reportes).toHaveCount(0);
    // El Dashboard es el botón de inicio, no una entrada más (spec-ui §6).
    await expect(page.getByRole("link", { name: "Dashboard", exact: true })).toHaveCount(0);

    await diagnosticos.click();
    await page.waitForURL(`${APP}/diagnosticos`);
    await expect(page.getByRole("heading", { level: 1, name: "Diagnóstico vehicular" })).toBeVisible();
    await expect(page.getByRole("tablist", { name: "Vista del inicio" }).getByRole("tab", { name: "Diagnósticos" })).toHaveAttribute("aria-selected", "true");
    // La entrada activa es «Diagnósticos», no el inicio.
    await expect(page.getByRole("link", { name: "Diagnósticos", exact: true }).first()).toHaveAttribute("aria-current", "page");
  });

  test("enlaces viejos: /?vista=vehiculos&placa=… y /?estado=…&pagina=2 llevan a /diagnosticos con la misma query", async ({ page }) => {
    exigirCliente();
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, "/");
    await page.goto(`${APP}/?vista=vehiculos&placa=ABC`);
    await expect(page).toHaveURL(`${APP}/diagnosticos?vista=vehiculos&placa=ABC`);
    await expect(page.getByRole("tablist", { name: "Vista del inicio" }).getByRole("tab", { name: "Vehículos" })).toHaveAttribute("aria-selected", "true");
    await page.goto(`${APP}/?estado=COMPLETADO&pagina=2`);
    await expect(page).toHaveURL(`${APP}/diagnosticos?estado=COMPLETADO&pagina=2`);
  });
});

test.describe("FE-5 Dashboard de la compañía", () => {
  test("vista de compañía: últimos 30 días por GET /estadisticas, sin selector ni top y sin pedir el backoffice", async ({ page }) => {
    exigirCliente();
    test.setTimeout(300_000);
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, "/diagnosticos");
    const sesion = await leerSesion(page);
    test.skip(tieneVistaGlobal(sesion), "E2E_USUARIO tiene vista global: no sirve para la vista de compañía");
    // Siembra: al menos un diagnóstico en el periodo, para ver el estado «lleno».
    const placa = placaFicticia(prefijoFicticio(), "1");
    await sembrarExpress(page, placa, { nombre: `E2E dashboard ${Date.now()}` });

    const backoffice = registrarPeticiones(page, RUTA.backoffice);
    const r = await abrirDashboard(page, RUTA.estadisticas);
    expect(r.status()).toBe(200);
    const url = new URL(r.url());
    const hoy = hoyBogota();
    expect(url.searchParams.get("hasta")).toBe(hoy);
    expect([29, 30], "rango por defecto de 30 días").toContain(diasEntre(url.searchParams.get("desde")!, hoy));
    expect(url.searchParams.has("empresa")).toBe(false);
    const cuerpo = await r.json();
    expect(cuerpo.ambito.tipo).toBe("EMPRESA");
    expect(cuerpo.resumen.diagnosticos).toBeGreaterThan(0);

    await expect(page.getByText(/^Resumen de diagnósticos de /)).toBeVisible();
    if (sesion.empresa.nombre) await expect(page.getByText(`Resumen de diagnósticos de ${sesion.empresa.nombre}`)).toBeVisible();
    await expect(selectorCompania(page)).toHaveCount(0);
    await expect(page.getByText("Vista global · todas las compañías")).toHaveCount(0);
    await expect(page.getByRole("heading", { name: "Compañías con más uso" })).toHaveCount(0);
    for (const s of SECCIONES) await expect(page.getByRole("heading", { level: 2, name: s }), `sección «${s}»`).toBeVisible();
    // La URL lleva el periodo (FE-5 «vista global y selector»: desde y hasta; empresa solo en el backoffice).
    await expect(page).toHaveURL(new RegExp(`[?&]desde=${url.searchParams.get("desde")}`));
    await expect(page).toHaveURL(new RegExp(`[?&]hasta=${hoy}`));
    // La cifra del KPI de diagnósticos se pinta con separador es-CO.
    const cifra = new Intl.NumberFormat("es-CO").format(cuerpo.resumen.diagnosticos);
    await expect(page.getByText(cifra, { exact: true }).first()).toBeVisible();
    // Cero PII: ni la placa sembrada ni ningún documento.
    await expect(page.getByText(placa)).toHaveCount(0);
    expect(backoffice.map((u) => u.pathname), "un usuario de compañía no pide rutas del backoffice").toEqual([]);

    // Accesibilidad de las gráficas: figure con figcaption y datos alternativos en tabla (o <details>).
    const figuras = await page.getByRole("figure").all();
    expect(figuras.length).toBeGreaterThan(0);
    for (const f of figuras) {
      await expect(f).toHaveAccessibleName(/\S/);
      expect(await f.locator("figcaption").count(), "figcaption visible").toBe(1);
      expect((await f.locator("table").count()) + (await f.locator("details").count()), "datos alternativos").toBeGreaterThan(0);
    }

    await revisarTemasOla(page, "dashboard-compania-1280", { captura: "D-1" });
    await sinDesbordeHorizontal(page, "dashboard-1280");
    await recorrerConTab(page, "dashboard-1280", { pasos: 40 });
    await ancho(page, 360);
    await revisarTemasOla(page, "dashboard-compania-360", { captura: "D-1" });
    await sinDesbordeHorizontal(page, "dashboard-360");
    await ancho(page, 1280);
  });

  test("filtros de fecha: más de 366 días no se envía y explica el límite; un 400 RANGO_INVALIDO muestra el texto genérico", async ({ page }) => {
    exigirCliente();
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, "/");
    await abrirDashboard(page, RUTA.estadisticas);
    const pedidas = registrarPeticiones(page, RUTA.estadisticas);
    const hoy = hoyBogota();
    await desde(page).fill(sumarDias(hoy, -400));
    await expect(page.getByText("El rango no puede superar un año")).toBeVisible();
    await expect(hasta(page)).toHaveAttribute("aria-invalid", "true");
    await page.waitForTimeout(800);
    expect(pedidas.filter((u) => u.searchParams.get("desde") === sumarDias(hoy, -400)), "no pide un rango inválido").toEqual([]);
    await revisarTemasOla(page, "dashboard-rango-invalido-1280", { captura: "D-2", paginaCompleta: false });

    // Un rango válido vuelve a pedir y quita el error.
    const valido = page.waitForResponse((r) => RUTA.estadisticas.test(r.url()) && new URL(r.url()).searchParams.get("desde") === sumarDias(hoy, -60));
    await desde(page).fill(sumarDias(hoy, -60));
    expect((await valido).status()).toBe(200);
    await expect(page.getByText("El rango no puede superar un año")).toHaveCount(0);

    // El servidor también lo rechaza (contrato §0) sin decir la causa: con el rango válido en el cliente, el texto
    // genérico (CR-FE-4), sin el mensaje del servidor.
    const fallo = await responder(page, RUTA.estadisticas, 400, { code: "RANGO_INVALIDO", error: "Mensaje del servidor de prueba." });
    await desde(page).fill(sumarDias(hoy, -10));
    await expect(page.getByText(/^Revisa el periodo: como máximo un año/)).toBeVisible();
    await expect(page.getByText("Mensaje del servidor de prueba.")).toHaveCount(0);
    await fallo.quitar();
  });

  test("estados: cargando, vacío con «Ver los últimos 90 días», error con «Reintentar» y 403 a la página 403", async ({ page }) => {
    exigirCliente();
    test.setTimeout(180_000);
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, "/");
    const sesion = await leerSesion(page);
    test.skip(tieneVistaGlobal(sesion), "E2E_USUARIO tiene vista global");

    await test.step("cargando (respuesta real retenida)", async () => {
      const espera = await retener(page, RUTA.estadisticas);
      await page.goto(`${APP}/`);
      await expect(H1(page)).toBeVisible();
      const cargando = page.locator('[role="status"][aria-busy="true"]').first();
      await expect(cargando).toBeVisible();
      await revisarTemasOla(page, "dashboard-cargando-1280", { captura: "D-3", axe: false });
      await espera.soltar();
      await expect(page.locator('[role="status"][aria-busy="true"]')).toHaveCount(0);
    });

    await test.step("vacío (EstadisticasDto del contrato con ceros)", async () => {
      const hoy = hoyBogota();
      const vacio = await responder(page, RUTA.estadisticas, 200, estadisticasVacias(sumarDias(hoy, -29), hoy));
      await page.goto(`${APP}/`);
      await expect(page.getByText("Aún no hay diagnósticos en este periodo")).toBeVisible();
      await expect(page.getByText("Cambia el rango de fechas o crea un diagnóstico.")).toBeVisible();
      const noventa = page.getByRole("button", { name: "Ver los últimos 90 días" }).or(page.getByRole("link", { name: "Ver los últimos 90 días" }));
      await expect(noventa).toBeVisible();
      if (sesion.capacidades.crearDiagnosticos) await expect(page.getByRole("link", { name: /Nuevo diagnóstico/ }).or(page.getByRole("button", { name: /Nuevo diagnóstico/ })).first()).toBeVisible();
      await revisarTemasOla(page, "dashboard-vacio-1280", { captura: "D-3" });
      const pedido = page.waitForRequest((r) => RUTA.estadisticas.test(r.url()) && new URL(r.url()).searchParams.get("desde") === sumarDias(hoy, -89));
      await noventa.click();
      await pedido;
      await vacio.quitar();
    });

    await test.step("error 500: «No se pudo cargar…» con «Reintentar» y sin el código técnico", async () => {
      const fallo = await fallarUnaVez(page, "GET", RUTA.estadisticas, { status: 500, code: "ERROR_INTERNO", error: "Fallo de prueba." }, { siempre: true });
      await page.goto(`${APP}/`);
      await expect(page.getByText(/^No se pudo cargar/).first()).toBeVisible();
      await expect(page.getByText("ERROR_INTERNO")).toHaveCount(0);
      await expect(page.getByText("Fallo de prueba.")).toHaveCount(0);
      await revisarTemasOla(page, "dashboard-error-1280", { captura: "D-3" });
      await fallo.quitar();
      const ok = page.waitForResponse((r) => RUTA.estadisticas.test(r.url()));
      await page.getByRole("button", { name: "Reintentar" }).first().click();
      expect((await ok).status()).toBe(200);
      await expect(page.getByText(/^No se pudo cargar/)).toHaveCount(0);
    });

    await test.step("403 SIN_PERMISO: la página 403 con el código", async () => {
      const negado = await responder(page, RUTA.estadisticas, 403, { code: "SIN_PERMISO", error: "Sin permiso." });
      await page.goto(`${APP}/`);
      await page.waitForURL(/\/403\?code=SIN_PERMISO/);
      await expect(page.getByRole("heading", { level: 1, name: "No tienes acceso" })).toBeVisible();
      await negado.quitar();
    });
  });
});

test.describe("FE-5 Dashboard del super admin", () => {
  test("abre en «Todas las compañías» (E2 global) con el top; el selector lista solo las compañías habilitadas", async ({ page }) => {
    exigirBackoffice();
    test.setTimeout(180_000);
    await entrar(page, BACKOFFICE.usuario!, BACKOFFICE.clave!, "/");
    const sesion = await leerSesion(page);
    test.skip(!tieneVistaGlobal(sesion), "El usuario de backoffice no tiene verEstadisticasGlobales");

    const empresasResp = await page.request.get(`${API}/backoffice/empresas`);
    expect(empresasResp.ok()).toBeTruthy();
    const empresas = (await empresasResp.json()) as { id: string; nombre: string; nit: string | null }[];

    const r = await abrirDashboard(page, RUTA.estadisticasGlobal);
    expect(r.status()).toBe(200);
    expect(new URL(r.url()).searchParams.has("empresa")).toBe(false);
    expect((await r.json()).ambito.tipo).toBe("GLOBAL");
    await expect(selectorCompania(page)).toHaveValue("Todas las compañías");
    await expect(page.getByText("Vista global · todas las compañías")).toBeVisible();
    await expect(page.getByText("Resumen de diagnósticos de todas las compañías")).toBeVisible();
    await expect(page.getByRole("heading", { name: "Compañías con más uso" })).toBeVisible();
    // Mientras carga, el selector no se deshabilita (spec-ui §4.4).
    await expect(selectorCompania(page)).toBeEnabled();

    // Opciones = «Todas las compañías» + GET /backoffice/empresas (que ya filtra las que tienen Diagnóstico activo).
    await selectorCompania(page).click();
    const lista = page.getByRole("listbox", { name: "Compañía" });
    await expect(lista).toBeVisible();
    const opciones = (await lista.getByRole("option").allTextContents()).map((t) => t.replace(/\s+/g, " ").trim());
    expect(opciones[0]).toBe("Todas las compañías");
    for (const e of empresas) expect(opciones.some((o) => o.includes(e.nombre)), `opción «${e.nombre}»`).toBe(true);
    expect(opciones.length, "ni una compañía de más que las habilitadas").toBe(empresas.length + 1);
    await page.keyboard.press("Escape");

    await revisarTemasOla(page, "dashboard-global-1280", { captura: "D-4" });
  });

  test("al elegir una compañía pide E3 ?empresa=, la URL lleva desde/hasta/empresa y desaparece el top", async ({ page }) => {
    exigirBackoffice();
    test.setTimeout(180_000);
    await entrar(page, BACKOFFICE.usuario!, BACKOFFICE.clave!, "/");
    const sesion = await leerSesion(page);
    test.skip(!tieneVistaGlobal(sesion), "El usuario de backoffice no tiene verEstadisticasGlobales");
    const empresas = (await (await page.request.get(`${API}/backoffice/empresas`)).json()) as { id: string; nombre: string }[];
    const empresa = empresas.find((e) => e.id === EMPRESA) ?? empresas[0];
    test.skip(!empresa, "No hay compañías con Diagnóstico activo");

    await abrirDashboard(page, RUTA.estadisticasGlobal);
    const r = page.waitForResponse((x) => RUTA.estadisticasEmpresa.test(x.url()) && new URL(x.url()).searchParams.get("empresa") === empresa.id);
    await selectorCompania(page).fill(empresa.nombre.slice(0, 6));
    await page.getByRole("listbox", { name: "Compañía" }).getByRole("option", { name: new RegExp(empresa.nombre.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")) }).first().click();
    const resp = await r;
    expect(resp.status()).toBe(200);
    expect((await resp.json()).ambito).toMatchObject({ tipo: "EMPRESA", empresa_id: empresa.id });
    await expect(page).toHaveURL(new RegExp(`[?&]empresa=${empresa.id}`));
    await expect(page).toHaveURL(/[?&]desde=\d{4}-\d{2}-\d{2}/);
    await expect(page).toHaveURL(/[?&]hasta=\d{4}-\d{2}-\d{2}/);
    await expect(page.getByText(`Resumen de diagnósticos de ${empresa.nombre}`)).toBeVisible();
    await expect(page.getByRole("heading", { name: "Compañías con más uso" })).toHaveCount(0);
    await expect(page.getByText("Vista global · todas las compañías")).toHaveCount(0);
    await revisarTemasOla(page, "dashboard-backoffice-compania-1280", { captura: "D-4" });

    // La URL con empresa se puede compartir: al recargar, sigue en esa compañía.
    const otra = page.waitForResponse((x) => RUTA.estadisticasEmpresa.test(x.url()));
    await page.reload();
    expect(new URL((await otra).url()).searchParams.get("empresa")).toBe(empresa.id);
  });

  test("503 de Identidad en el selector: queda deshabilitado con aviso y «Reintentar»; la vista global sigue", async ({ page }) => {
    exigirBackoffice();
    await entrar(page, BACKOFFICE.usuario!, BACKOFFICE.clave!, "/");
    const sesion = await leerSesion(page);
    test.skip(!tieneVistaGlobal(sesion), "El usuario de backoffice no tiene verEstadisticasGlobales");
    const caida = await responder(page, RUTA.empresas, 503, { code: "IDENTIDAD_NO_DISPONIBLE", error: "Identidad caída (prueba)." }, { "Retry-After": "30" });
    const r = await abrirDashboard(page, RUTA.estadisticasGlobal);
    expect(r.status()).toBe(200);
    await expect(page.getByText("No se pudo cargar la lista de compañías")).toBeVisible();
    await expect(selectorCompania(page)).toBeDisabled();
    await expect(page.getByRole("heading", { name: "Compañías con más uso" })).toBeVisible();
    await revisarTemasOla(page, "dashboard-selector-503-1280", { captura: "D-5", paginaCompleta: false });
    await caida.quitar();
    const lista = page.waitForResponse((x) => RUTA.empresas.test(x.url()));
    await page.getByRole("button", { name: "Reintentar" }).first().click();
    expect((await lista).status()).toBe(200);
    await expect(selectorCompania(page)).toBeEnabled();
  });

  test("503 de la vista global: «No se pudo cargar…» con «Reintentar» (sin el mensaje del servidor)", async ({ page }) => {
    exigirBackoffice();
    await entrar(page, BACKOFFICE.usuario!, BACKOFFICE.clave!, "/");
    const sesion = await leerSesion(page);
    test.skip(!tieneVistaGlobal(sesion), "El usuario de backoffice no tiene verEstadisticasGlobales");
    const caida = await responder(page, RUTA.estadisticasGlobal, 503, { code: "IDENTIDAD_NO_DISPONIBLE", error: "Identidad caída (prueba)." }, { "Retry-After": "30" });
    await page.goto(`${APP}/`);
    await expect(page.getByText(/^No se pudo cargar/).first()).toBeVisible();
    await expect(page.getByText("Identidad caída (prueba).")).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Reintentar" }).first()).toBeVisible();
    await caida.quitar();
  });
});
