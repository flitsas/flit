import { expect, test, type Page, type Route } from "@playwright/test";
import { ADMIN, leerSesion, registrarPeticiones, variarCapacidades } from "./ola-ux-comun";
import { APP, BACKOFFICE, CLIENTE, ancho, entrar, exigirBackoffice, exigirCliente, sinDesbordeHorizontal } from "./saneamiento-comun";
import { RUTA_REQ, capturar, revisarTemasReq } from "./ola-requerimiento-comun";

/**
 * HU #14588 · Módulo Usuarios de Diagnóstico (oleada «requerimiento.pdf», 2026-11-16). spec-ui §9–§10 (A9.4, A10.1–A10.7)
 * con los ajustes H-UI de contrato-api.md §5 (sin «Último ingreso», sin acciones en Invitado, aviso uniforme al invitar, sin
 * filtro de estado de compañías); SP-10, SP-11=a, SP-12=a, SP-15, SP-17, SP-20.
 *
 * Modo de la API: por defecto (E2E_USUARIOS_MOCK distinto de «0») las rutas de Usuarios se simulan con `page.route` según el
 * contrato §3.3, porque be2b aún no está integrado; la capacidad `gestionarUsuarios` se fuerza en la respuesta REAL de
 * /sesion. Con E2E_USUARIOS_MOCK=0 las LECTURAS van a la suite real; las escrituras (invitar, suspender) se simulan siempre
 * para no mandar correos ni tocar roles de la compañía de prueba. Datos: placeholders `@ejemplo.com`, nunca PII real (A10.7).
 * Variables: E2E_USUARIO / E2E_CLAVE (usuario_diagnostico, sin gestionarUsuarios), E2E_ADMIN_USUARIO / E2E_ADMIN_CLAVE
 * (admin_diagnostico) y E2E_BACKOFFICE_USUARIO / E2E_BACKOFFICE_CLAVE (SuperAdmin).
 *
 * Lista congelada (pantallas nuevas de fe1-usuarios; ajustar en la Fase 3 si el nombre final cambia):
 *   - link «Usuarios» del dock (href /usuarios) · heading h1 «/^Usuarios/» · button «Invitar usuario»
 *   - table de usuarios (primera table de main) · columnheader «Nombre», «Correo», «Rol», «Estado», «Acciones» · fila por
 *     nombre · button «/^Acciones/» en la fila (nombre accesible con el nombre, nunca el correo)
 *   - textbox «Buscar por nombre o correo» · combobox o group «Estado» · button «Limpiar filtros» ·
 *     texto «Mostrando N de M usuarios» · «Ningún usuario coincide»
 *   - menuitem «/^Cambiar rol/» · «/^Suspender/» · «/^Reactivar/»
 *   - dialog «Invitar usuario» · textbox «Nombre completo» · textbox «Correo electrónico» · radio «/Usuario de Diagnóstico/» /
 *     «/Administrador de Diagnóstico/» · button «Enviar invitación» · button «Cancelar»
 *   - dialog «/Suspender/» con foco inicial en button «Cancelar» y button «Suspender»
 *   - table de compañías · columnheader «Compañía», «NIT», «Usuarios», «Administradores» · link «Gestionar usuarios de <Compañía>»
 *   - navigation «Migas de pan» · link «Usuarios» · elemento aria-current=page «<Compañía>»
 */
test.describe.configure({ mode: "serial" });

const SPEC = "usuarios";
const MOCK = process.env.E2E_USUARIOS_MOCK !== "0";
const AVISO_INVITACION = "Si el correo es válido, la persona recibirá la invitación o el acceso a Diagnóstico.";
const EMPRESA_DEMO = { id: "00000000-0000-4000-8000-0000000000e1", nombre: "Compañía Demo", nit: "900000001" };
const OTRA_EMPRESA = { id: "00000000-0000-4000-8000-0000000000e2", nombre: "Compañía Ejemplo", nit: "900000002" };
const ID = { uno: "00000000-0000-4000-8000-000000000001", dos: "00000000-0000-4000-8000-000000000002", tres: "00000000-0000-4000-8000-000000000003" };

type Estado = "ACTIVO" | "INVITADO" | "SUSPENDIDO";
type Rol = "usuario_diagnostico" | "admin_diagnostico";
type Accion = "CAMBIAR_ROL" | "SUSPENDER" | "REACTIVAR";
interface UsuarioDto {
  id: string;
  tipo: "USUARIO" | "INVITACION";
  nombre: string;
  correo: string;
  rol: Rol;
  rol_etiqueta: string;
  estado: Estado;
  acciones: Accion[];
  motivo_sin_acciones: string | null;
}

const ETIQUETA_ROL: Record<Rol, string> = { usuario_diagnostico: "Usuario de Diagnóstico", admin_diagnostico: "Administrador de Diagnóstico" };

function fila(id: string, nombre: string, correo: string, rol: Rol, estado: Estado, acciones: Accion[], motivo: string | null, tipo: UsuarioDto["tipo"] = "USUARIO"): UsuarioDto {
  return { id, tipo, nombre, correo, rol, rol_etiqueta: ETIQUETA_ROL[rol], estado, acciones, motivo_sin_acciones: motivo };
}

/** Fixture del contrato §3.3: la fila propia (sin acciones, USTED_MISMO), una activa, una suspendida y una invitación. */
function usuariosDe(propio: string): UsuarioDto[] {
  return [
    fila(propio, "Admin Demo", "admin.demo@ejemplo.com", "admin_diagnostico", "ACTIVO", [], "USTED_MISMO"),
    fila(ID.uno, "Usuario Uno", "usuario.uno@ejemplo.com", "usuario_diagnostico", "ACTIVO", ["CAMBIAR_ROL", "SUSPENDER"], null),
    fila(ID.dos, "Usuario Dos", "usuario.dos@ejemplo.com", "usuario_diagnostico", "SUSPENDIDO", ["REACTIVAR"], null),
    fila(ID.tres, "Invitada Tres", "invitada.tres@ejemplo.com", "usuario_diagnostico", "INVITADO", [], "INVITACION", "INVITACION"),
  ];
}

const sinTildes = (t: string) => t.normalize("NFD").replace(/\p{Diacritic}/gu, "").toLowerCase();

interface Simulacion {
  invitaciones: Record<string, unknown>[];
  suspendidos: string[];
  lecturas: () => number;
}

/** Simula /usuarios* (y su gemelo del backoffice) según el contrato; filtra por busqueda/rol/estado como el servidor. */
async function simularUsuarios(page: Page, opciones: { propio: string; roles: Rol[]; lecturasReales?: boolean }): Promise<Simulacion> {
  const filas = usuariosDe(opciones.propio);
  let lecturas = 0;
  const sim: Simulacion = { invitaciones: [], suspendidos: [], lecturas: () => lecturas };
  const lista = async (route: Route) => {
    if (route.request().method() !== "GET") return route.fallback();
    lecturas++;
    if (opciones.lecturasReales) return route.fallback();
    const q = new URL(route.request().url()).searchParams;
    const busqueda = sinTildes(q.get("busqueda") ?? "");
    const roles = q.getAll("rol");
    const estados = q.getAll("estado");
    const usuarios = filas.filter(
      (f) =>
        (!busqueda || sinTildes(f.nombre).includes(busqueda) || sinTildes(f.correo).includes(busqueda)) &&
        (!roles.length || roles.includes(f.rol)) &&
        (!estados.length || estados.includes(f.estado)),
    );
    return route.fulfill({ json: { pagina: 1, tamano: 25, total: usuarios.length, roles_asignables: opciones.roles, usuarios } });
  };
  await page.route(RUTA_REQ.usuarios, lista);
  await page.route(RUTA_REQ.usuariosEmpresa, lista);
  await page.route(RUTA_REQ.invitaciones, async (route) => {
    if (route.request().method() !== "POST") return route.fallback();
    sim.invitaciones.push(route.request().postDataJSON() as Record<string, unknown>);
    return route.fulfill({ status: 202, json: { resultado: "ACEPTADA", mensaje: AVISO_INVITACION } });
  });
  await page.route(RUTA_REQ.suspension, async (route) => {
    const id = new URL(route.request().url()).pathname.split("/").at(-2)!;
    const f = filas.find((x) => x.id === id);
    if (!f) return route.fulfill({ status: 404, json: { code: "USUARIO_NO_ENCONTRADO", error: "No encontramos ese usuario." } });
    if (route.request().method() === "POST") {
      sim.suspendidos.push(id);
      Object.assign(f, { estado: "SUSPENDIDO", acciones: ["REACTIVAR"] });
    } else Object.assign(f, { estado: "ACTIVO", acciones: ["CAMBIAR_ROL", "SUSPENDER"] });
    return route.fulfill({ json: f });
  });
  return sim;
}

async function simularEmpresas(page: Page) {
  const empresas = [
    { ...EMPRESA_DEMO, estado: "ACTIVA", usuarios_diagnostico: 4, administradores_diagnostico: 1 },
    { ...OTRA_EMPRESA, estado: "ACTIVA", usuarios_diagnostico: 0, administradores_diagnostico: 0 },
  ];
  await page.route(RUTA_REQ.empresasUsuarios, async (route) => {
    if (route.request().method() !== "GET") return route.fallback();
    const q = new URL(route.request().url()).searchParams;
    const b = sinTildes(q.get("busqueda") ?? "");
    const u = q.get("usuarios");
    const lista = empresas.filter(
      (e) => (!b || sinTildes(e.nombre).includes(b) || e.nit.startsWith(b)) && (!u || (u === "CON" ? e.usuarios_diagnostico > 0 : e.usuarios_diagnostico === 0)),
    );
    return route.fulfill({ json: { pagina: 1, tamano: 25, total: lista.length, empresas: lista } });
  });
}

/** Entra con el perfil y, en modo simulado, fuerza `gestionarUsuarios` en la respuesta real de /sesion. */
async function entrarCon(page: Page, usuario: string, clave: string, gestionar: boolean) {
  await page.setViewportSize({ width: 1280, height: 800 });
  await entrar(page, usuario, clave, "/diagnosticos");
  const sesion = await leerSesion(page);
  if (MOCK) await variarCapacidades(page, { gestionarUsuarios: gestionar });
  return sesion;
}

const tablaUsuarios = (page: Page) => page.getByRole("main").getByRole("table").first();
const filaDe = (page: Page, nombre: string) => tablaUsuarios(page).getByRole("row").filter({ hasText: nombre });
const accionesDe = (page: Page, nombre: string) => filaDe(page, nombre).getByRole("button", { name: /^Acciones/ });

test.describe("HU #14588 Usuarios de Diagnóstico", () => {
  test("visibilidad: el usuario sin gestionarUsuarios no ve «Usuarios» en el dock y la ruta le muestra la 403", async ({ page }) => {
    exigirCliente();
    const sesion = await entrarCon(page, CLIENTE.usuario!, CLIENTE.clave!, false);
    test.skip(!MOCK && Boolean(sesion.capacidades.gestionarUsuarios), "E2E_USUARIO gestiona usuarios: no sirve para este caso");
    await page.reload();
    await expect(page.getByRole("link", { name: "Usuarios", exact: true })).toHaveCount(0);
    await page.goto(`${APP}/usuarios`);
    await page.waitForURL(/\/403/);
    await expect(page.getByRole("heading", { level: 1, name: "No tienes acceso" })).toBeVisible();
    await capturar(page, SPEC, 1, "sin-capacidad-403");
  });

  test("admin: entra desde el dock; tabla con la receta, sin «Último ingreso», estado con texto y sin compañías", async ({ page }) => {
    test.skip(!ADMIN.usuario || !ADMIN.clave, "Faltan E2E_ADMIN_USUARIO / E2E_ADMIN_CLAVE");
    const sesion = await entrarCon(page, ADMIN.usuario!, ADMIN.clave!, true);
    await simularUsuarios(page, { propio: sesion.usuario.id, roles: ["usuario_diagnostico"], lecturasReales: !MOCK });
    const backoffice = registrarPeticiones(page, RUTA_REQ.empresasUsuarios);
    await page.reload();

    const enlace = page.getByRole("link", { name: "Usuarios", exact: true }).first();
    await expect(enlace).toHaveAttribute("href", /\/usuarios$/);
    await enlace.click();
    await page.waitForURL((u) => u.pathname === "/usuarios" && !u.searchParams.has("empresa"));
    await expect(page.getByRole("heading", { level: 1, name: /^Usuarios/ })).toBeVisible();
    await expect(page.getByRole("button", { name: "Invitar usuario" })).toBeVisible();

    const tabla = tablaUsuarios(page);
    await expect(tabla).toBeVisible();
    for (const col of ["Nombre", "Correo", "Rol", "Estado", "Acciones"]) await expect(tabla.getByRole("columnheader", { name: col, exact: true })).toBeVisible();
    await expect(tabla.getByRole("columnheader", { name: /Último ingreso/ })).toHaveCount(0);
    if (MOCK) {
      await expect(filaDe(page, "Usuario Uno")).toContainText("Usuario de Diagnóstico");
      await expect(filaDe(page, "Usuario Dos")).toContainText("Suspendido");
      await expect(filaDe(page, "Invitada Tres")).toContainText("Invitado");
      // H-UI (2): la invitación no tiene acciones en esta ola.
      await expect(accionesDe(page, "Invitada Tres")).toHaveCount(0);
    }
    // A10.2: el admin nunca pide la tabla de compañías.
    expect(backoffice, "el admin no consulta /backoffice/usuarios/empresas").toHaveLength(0);
    // SP-20: ningún aria-label de la tabla lleva un correo.
    const conCorreo = await tabla.locator("[aria-label]").evaluateAll((ns) => ns.map((n) => n.getAttribute("aria-label") ?? "").filter((a) => a.includes("@")));
    expect(conCorreo).toEqual([]);
    await capturar(page, SPEC, 2, "admin-tabla-usuarios", { paginaCompleta: true });
    await revisarTemasReq(page, "usuarios-admin-1280", { captura: "US-1" });
    await ancho(page, 360);
    await sinDesbordeHorizontal(page, "usuarios-admin-360");
    await capturar(page, SPEC, 3, "admin-tabla-360", { paginaCompleta: true });
    await ancho(page, 1280);
  });

  test("admin: buscador y filtro de estado, «Limpiar filtros» y vacío por filtro", async ({ page }) => {
    test.skip(!ADMIN.usuario || !ADMIN.clave, "Faltan E2E_ADMIN_USUARIO / E2E_ADMIN_CLAVE");
    test.skip(!MOCK, "Los conteos dependen del fixture");
    const sesion = await entrarCon(page, ADMIN.usuario!, ADMIN.clave!, true);
    await simularUsuarios(page, { propio: sesion.usuario.id, roles: ["usuario_diagnostico"] });
    await page.goto(`${APP}/usuarios`);
    await expect(filaDe(page, "Usuario Uno")).toBeVisible();
    await expect(page.getByText(/^Mostrando 4 de 4 usuarios$/)).toBeVisible();

    const buscar = page.getByRole("textbox", { name: "Buscar por nombre o correo" });
    await buscar.fill("uno");
    await expect(filaDe(page, "Usuario Uno")).toBeVisible();
    await expect(filaDe(page, "Usuario Dos")).toHaveCount(0);
    await capturar(page, SPEC, 4, "admin-buscar");

    await page.getByRole("button", { name: "Limpiar filtros" }).first().click();
    const estado = page.getByRole("combobox", { name: "Estado" }).or(page.getByRole("group", { name: "Estado" })).first();
    if ((await estado.evaluate((n) => n.tagName)) === "SELECT") await estado.selectOption({ label: "Suspendido" });
    else await estado.getByRole("button", { name: /^Suspendido/ }).click();
    await expect(filaDe(page, "Usuario Dos")).toBeVisible();
    await expect(filaDe(page, "Usuario Uno")).toHaveCount(0);
    await capturar(page, SPEC, 5, "admin-filtro-estado");

    await buscar.fill("zzz-nadie");
    await expect(page.getByText("Ningún usuario coincide", { exact: true })).toBeVisible();
    await capturar(page, SPEC, 6, "admin-vacio-por-filtro");
    await page.getByRole("button", { name: "Limpiar filtros" }).last().click();
    await expect(filaDe(page, "Usuario Uno")).toBeVisible();
  });

  test("admin: invitar solo con el rol que puede dar, código de rol en el cuerpo y aviso uniforme; errores por campo", async ({ page }) => {
    test.skip(!ADMIN.usuario || !ADMIN.clave, "Faltan E2E_ADMIN_USUARIO / E2E_ADMIN_CLAVE");
    const sesion = await entrarCon(page, ADMIN.usuario!, ADMIN.clave!, true);
    const sim = await simularUsuarios(page, { propio: sesion.usuario.id, roles: ["usuario_diagnostico"], lecturasReales: !MOCK });
    await page.goto(`${APP}/usuarios`);
    const invitar = page.getByRole("button", { name: "Invitar usuario" });
    await invitar.click();
    const dialogo = page.getByRole("dialog", { name: "Invitar usuario" });
    await expect(dialogo).toBeVisible();
    const nombre = dialogo.getByRole("textbox", { name: "Nombre completo" });
    const correo = dialogo.getByRole("textbox", { name: "Correo electrónico" });
    await expect(nombre).toBeFocused();

    // A10.3 / SP-11=a: el admin solo ofrece «Usuario de Diagnóstico» (roles_asignables); nunca roles de otros productos.
    const total = await dialogo.getByRole("radio").count();
    expect(total).toBeLessThanOrEqual(1);
    await expect(dialogo.getByRole("radio", { name: /Administrador de Diagnóstico/ })).toHaveCount(0);
    if (total) await expect(dialogo.getByRole("radio", { name: /Usuario de Diagnóstico/ })).toHaveCount(1);

    // Errores por campo: aria-invalid y descripción.
    await dialogo.getByRole("button", { name: "Enviar invitación" }).click();
    await expect(nombre).toHaveAttribute("aria-invalid", "true");
    await expect(nombre).toHaveAccessibleDescription(/\S/);
    await capturar(page, SPEC, 7, "invitar-errores-por-campo");

    await nombre.fill("Persona Invitada");
    await correo.fill("persona.invitada@ejemplo.com");
    if (total) await dialogo.getByRole("radio", { name: /Usuario de Diagnóstico/ }).check();
    const antes = sim.lecturas();
    await capturar(page, SPEC, 8, "invitar-formulario-lleno");
    await dialogo.getByRole("button", { name: "Enviar invitación" }).click();
    await expect(dialogo).toHaveCount(0);
    // SP-15 / H-UI (3): el aviso es el texto uniforme del 202, no «Invitación enviada a …».
    await expect(page.getByText(AVISO_INVITACION, { exact: true })).toBeVisible();
    await expect(page.getByText(/Invitación enviada a/)).toHaveCount(0);
    await expect.poll(() => sim.lecturas(), { message: "refresca la lista" }).toBeGreaterThan(antes);
    // SP-10: solo código de rol, sin rol_id ni otros campos.
    expect(sim.invitaciones).toHaveLength(1);
    expect(Object.keys(sim.invitaciones[0]).sort()).toEqual(["correo", "nombre", "rol"]);
    expect(sim.invitaciones[0].rol).toBe("usuario_diagnostico");
    await expect(invitar).toBeFocused();
    await capturar(page, SPEC, 9, "invitar-aviso-uniforme");
  });

  test("admin: suspender con foco inicial en «Cancelar», Esc devuelve el foco; confirmar deja «Suspendido»", async ({ page }) => {
    test.skip(!ADMIN.usuario || !ADMIN.clave, "Faltan E2E_ADMIN_USUARIO / E2E_ADMIN_CLAVE");
    test.skip(!MOCK, "Suspender usa el fixture");
    const sesion = await entrarCon(page, ADMIN.usuario!, ADMIN.clave!, true);
    const sim = await simularUsuarios(page, { propio: sesion.usuario.id, roles: ["usuario_diagnostico"] });
    await page.goto(`${APP}/usuarios`);
    const disparador = accionesDe(page, "Usuario Uno");
    await expect(disparador).toBeVisible();
    // SP-20: el nombre accesible del disparador y de las acciones no lleva el correo.
    expect((await disparador.getAttribute("aria-label")) ?? "").not.toContain("@");
    await disparador.click();
    const menu = page.getByRole("menu");
    await expect(menu.getByRole("menuitem", { name: /^Cambiar rol/ })).toBeVisible();
    const suspender = menu.getByRole("menuitem", { name: /^Suspender/ });
    expect((await suspender.getAttribute("aria-label")) ?? "").not.toContain("@");
    await suspender.click();
    const dialogo = page.getByRole("dialog", { name: /Suspender/ });
    await expect(dialogo).toBeVisible();
    // A10.4: foco inicial en «Cancelar»; el texto dice que solo pierde Diagnóstico.
    await expect(dialogo.getByRole("button", { name: "Cancelar" })).toBeFocused();
    await expect(dialogo).toContainText(/no podrá entrar a Diagnóstico/);
    await capturar(page, SPEC, 10, "suspender-foco-en-cancelar");
    await page.keyboard.press("Escape");
    await expect(dialogo).toHaveCount(0);
    await expect(disparador).toBeFocused();
    expect(sim.suspendidos).toEqual([]);

    await disparador.click();
    await page.getByRole("menu").getByRole("menuitem", { name: /^Suspender/ }).click();
    await page.getByRole("dialog", { name: /Suspender/ }).getByRole("button", { name: "Suspender", exact: true }).click();
    await expect(page.getByRole("dialog")).toHaveCount(0);
    expect(sim.suspendidos).toEqual([ID.uno]);
    await expect(filaDe(page, "Usuario Uno")).toContainText("Suspendido");
    await capturar(page, SPEC, 11, "suspender-confirmado");
    await revisarTemasReq(page, "usuarios-suspendido-1280", { captura: "US-2" });
  });

  test("admin: la propia fila no tiene ninguna acción habilitada (SP-20 / spec-ui §10.1)", async ({ page }) => {
    test.skip(!ADMIN.usuario || !ADMIN.clave, "Faltan E2E_ADMIN_USUARIO / E2E_ADMIN_CLAVE");
    test.skip(!MOCK, "La fila propia sale del fixture");
    const sesion = await entrarCon(page, ADMIN.usuario!, ADMIN.clave!, true);
    await simularUsuarios(page, { propio: sesion.usuario.id, roles: ["usuario_diagnostico"] });
    await page.goto(`${APP}/usuarios`);
    await expect(filaDe(page, "Admin Demo")).toBeVisible();
    // spec-ui §10.1 (deshabilitadas con motivo) o SP-20 (sin acciones): en ningún caso hay una acción habilitada.
    const propio = accionesDe(page, "Admin Demo");
    if ((await propio.count()) && (await propio.isEnabled())) {
      await propio.click();
      const items = page.getByRole("menu").getByRole("menuitem");
      for (let i = 0; i < (await items.count()); i++) await expect(items.nth(i)).toHaveAttribute("aria-disabled", "true");
      await capturar(page, SPEC, 12, "propia-fila-acciones-deshabilitadas");
      await page.keyboard.press("Escape");
    } else {
      await capturar(page, SPEC, 12, "propia-fila-sin-acciones");
    }
  });

  test("SuperAdmin: compañías → usuarios de una compañía → migas de vuelta; invitar ofrece los dos roles", async ({ page }) => {
    exigirBackoffice();
    const sesion = await entrarCon(page, BACKOFFICE.usuario!, BACKOFFICE.clave!, true);
    if (MOCK) await simularEmpresas(page);
    await simularUsuarios(page, { propio: sesion.usuario.id, roles: ["usuario_diagnostico", "admin_diagnostico"], lecturasReales: !MOCK });
    const deEmpresa = registrarPeticiones(page, RUTA_REQ.usuariosEmpresa);
    await page.goto(`${APP}/usuarios`);

    // Paso 1: tabla de compañías (sin filtro de estado, H-UI 4).
    const companias = page.getByRole("main").getByRole("table").first();
    await expect(companias).toBeVisible();
    for (const col of ["Compañía", "NIT", "Usuarios", "Administradores"]) await expect(companias.getByRole("columnheader", { name: col, exact: true })).toBeVisible();
    await expect(page.getByRole("combobox", { name: /^Estado/ })).toHaveCount(0);
    await capturar(page, SPEC, 13, "superadmin-companias", { paginaCompleta: true });
    await revisarTemasReq(page, "usuarios-companias-1280", { captura: "US-3" });

    const nombreEmpresa = MOCK ? EMPRESA_DEMO.nombre : ((await companias.getByRole("row").nth(1).getByRole("cell").first().textContent()) ?? "").trim();

    // O-1: el texto buscado (nombre o NIT, que puede ser una cédula) llega a la API pero no a la URL de la pantalla.
    const buscarCompania = page.getByRole("textbox", { name: "Buscar por nombre o NIT" });
    const conBusqueda = page.waitForRequest(
      (r) => new URL(r.url()).pathname.endsWith("/backoffice/usuarios/empresas") && new URL(r.url()).searchParams.get("busqueda") === nombreEmpresa,
    );
    await buscarCompania.fill(nombreEmpresa);
    await conBusqueda;
    expect(new URL(page.url()).searchParams.has("busqueda")).toBe(false);
    await page.getByRole("link", { name: `Gestionar usuarios de ${nombreEmpresa}` }).click();
    await page.waitForURL((u) => u.pathname === "/usuarios" && u.searchParams.has("empresa"));
    if (MOCK) expect(new URL(page.url()).searchParams.get("empresa")).toBe(EMPRESA_DEMO.id);
    expect(new URL(page.url()).searchParams.has("busqueda")).toBe(false);

    // Paso 2: las lecturas van al grupo del backoffice; migas «Usuarios › Compañía».
    await expect.poll(() => deEmpresa.length, { message: "GET /backoffice/empresas/{empresa}/usuarios" }).toBeGreaterThan(0);
    const migas = page.getByRole("navigation", { name: "Migas de pan" });
    await expect(migas.getByRole("link", { name: "Usuarios", exact: true })).toBeVisible();
    await expect(migas.locator('[aria-current="page"]')).toHaveText(nombreEmpresa);
    await expect(tablaUsuarios(page)).toBeVisible();
    await capturar(page, SPEC, 14, "superadmin-usuarios-de-compania", { paginaCompleta: true });

    // El SuperAdmin ofrece los dos roles (SP-11=a).
    await page.getByRole("button", { name: "Invitar usuario" }).click();
    const dialogo = page.getByRole("dialog", { name: "Invitar usuario" });
    await expect(dialogo.getByRole("radio", { name: /Usuario de Diagnóstico/ })).toBeVisible();
    await expect(dialogo.getByRole("radio", { name: /Administrador de Diagnóstico/ })).toBeVisible();
    await capturar(page, SPEC, 15, "superadmin-invitar-dos-roles");
    await dialogo.getByRole("button", { name: "Cancelar" }).click();

    // Miga de vuelta: compañías sin `empresa` en la URL.
    await migas.getByRole("link", { name: "Usuarios", exact: true }).click();
    await page.waitForURL((u) => u.pathname === "/usuarios" && !u.searchParams.has("empresa"));
    await expect(page.getByRole("link", { name: `Gestionar usuarios de ${nombreEmpresa}` })).toBeVisible();
    // O-1: la búsqueda vuelve desde la pestaña (sessionStorage), no desde la URL.
    await expect(buscarCompania).toHaveValue(nombreEmpresa);
    expect(new URL(page.url()).searchParams.has("busqueda")).toBe(false);
    await capturar(page, SPEC, 16, "superadmin-vuelta-por-migas");
  });
});
