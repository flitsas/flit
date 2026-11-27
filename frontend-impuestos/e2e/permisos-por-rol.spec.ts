import { expect, test, type Locator, type Page } from "@playwright/test";
import {
  ADMIN,
  APP,
  EMPRESA,
  GESTOR,
  SUPER_ADMIN,
  alcanzarConTab,
  conEmpresa,
  csvPlacas,
  entradaDock,
  entrar,
  exigir,
  exigirEmpresa,
  exigirStub,
  placaFicticia,
  revisar360,
  revisarTemas,
} from "./comun";

/**
 * HU #14694 / #14695 · Permisos por rol (plan §5.2, §6.1; spec-visual §2, §3.8 y §4.5; contrato 1.2.1).
 * Criterios: F6-CF1 (login y pantalla principal), F6-CF3 (módulos por rol), F5b-CF1 (2 roles + SA de plataforma),
 * F5b-CF3..CF6 (gestión de usuarios y alcance por rol; delta 6 `protegido`, filtros Rol y Estado, columna Usuarios),
 * AC-FE-08 (literales de la 403).
 *
 * Modo de la API: suite real, sin `page.route`. Gestor = gestor_impuestos; Administrador = admin_impuestos (el AdminCompany
 * lo recibe solo); Super Admin de plataforma. La fila «protegido» (AdminCompany) se ve desde el Super Admin con ?empresa,
 * porque para el propio AdminCompany su fila dice «Eres tú».
 *
 * Lista de nombres accesibles (ImpuestosShell, app/403, Usuarios; ajustar en la Fase 3 si cambian):
 *   - navigation «Navegación principal» · link «Impuestos», «Reportes», «Usuarios», «Configuración», «Backoffice»
 *   - 403: h1 «No tienes acceso» · link «Ir a mis productos»
 *   - /usuarios: h1 «Usuarios» · table «Usuarios de Impuestos» · columnheader «Nombre», «Correo», «Rol», «Estado», «Acciones» ·
 *     combobox «Rol» / «Estado» · button «Acciones del usuario» · «Este usuario se gestiona desde el administrador de la
 *     plataforma.» · «Eres tú» · SA sin compañía: table «Compañías con Impuestos» con columnheader «Usuarios»
 */
test.describe.configure({ mode: "serial" });

const MODULOS = ["Impuestos", "Reportes", "Usuarios", "Configuración", "Backoffice"] as const;
type Modulo = (typeof MODULOS)[number];

async function esperarDock(page: Page, visibles: readonly Modulo[]) {
  await expect(entradaDock(page, "Impuestos")).toBeVisible();
  for (const m of MODULOS) {
    if (visibles.includes(m)) await expect(entradaDock(page, m), `dock: ${m} visible`).toBeVisible();
    else await expect(entradaDock(page, m), `dock: ${m} oculto`).toHaveCount(0);
  }
}

async function esperar403(page: Page, ruta: string) {
  await page.goto(`${APP}${ruta}`);
  await page.waitForURL(/\/403\?code=SIN_PERMISO/);
  await expect(page.getByRole("heading", { level: 1, name: "No tienes acceso" })).toBeVisible();
  await expect(page.getByText("No tienes permiso para abrir esta página.")).toBeVisible();
  await expect(page.getByRole("link", { name: "Ir a mis productos" })).toHaveAttribute("href", /\?inicio=1$/);
}

/** Filas de datos (las que tienen celdas) de una tabla. */
const filasDatos = (tabla: Locator, page: Page) => tabla.getByRole("row").filter({ has: page.getByRole("cell") });

test.describe("Gestor Impuestos", () => {
  test.beforeEach(async ({ page }) => {
    exigir(GESTOR, "Gestor");
    await entrar(page, GESTOR, "/");
  });

  test("QA_TC01_IMPUESTOS_Permisos - Gestor ve Impuestos y Reportes en el dock", async ({ page }) => {
    await expect(page.getByRole("heading", { level: 1, name: "Dashboard" })).toBeVisible();
    await esperarDock(page, ["Impuestos", "Reportes"]);
    await alcanzarConTab(page, entradaDock(page, "Reportes"));
    await revisarTemas(page, "dashboard-gestor", "PR-1");
    await revisar360(page, "dashboard-gestor", "PR-1");
  });

  test("QA_TC02_IMPUESTOS_Permisos - Gestor en Usuarios, Configuración o Backoffice llega a la 403", async ({ page }) => {
    for (const ruta of ["/usuarios", "/configuracion", "/backoffice/empresas"]) await esperar403(page, ruta);
  });

  test("QA_TC03_IMPUESTOS_Permisos - Gestor crea un lote pero no puede cerrarlo", async ({ page, request }) => {
    await exigirStub(request);
    await page.goto(`${APP}/liquidaciones/masiva`);
    await page.getByLabel("Archivo de placas (.xlsx o .csv)").setInputFiles(csvPlacas([placaFicticia(), "TMO000"]));
    await page.getByRole("button", { name: "Analizar archivo" }).click();
    await page.getByRole("button", { name: "Iniciar liquidación de 2 placas" }).click();
    await page.waitForURL(/\/liquidaciones\/[0-9a-f-]{36}(\?|$)/i);
    await expect(page.getByRole("progressbar", { name: "Avance del lote" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Cerrar lote" })).toHaveCount(0);
  });

  test("QA_TC04_IMPUESTOS_Permisos - ?empresa de quien no es Super Admin no cambia de compañía", async ({ page }) => {
    // Para otro usuario ?empresa= no cuenta en la UI; la API respondería 403 EMPRESA_SOLO_SUPER_ADMIN (delta 8).
    const r = await page.request.get(`${APP}/api/v1/impuestos/liquidaciones?empresa=00000000-0000-4000-8000-0000000000e2`);
    expect(r.status()).toBe(403);
    expect(((await r.json()) as { code?: string }).code).toBe("EMPRESA_SOLO_SUPER_ADMIN");
  });
});

test.describe("Administrador Impuestos", () => {
  test.beforeEach(async ({ page }) => {
    exigir(ADMIN, "Administrador");
    await entrar(page, ADMIN, "/");
  });

  test("QA_TC05_IMPUESTOS_Permisos - Administrador ve Usuarios y Configuración pero no Backoffice", async ({ page }) => {
    await esperarDock(page, ["Impuestos", "Reportes", "Usuarios", "Configuración"]);
    await esperar403(page, "/backoffice/empresas");
  });

  test("QA_TC06_IMPUESTOS_Permisos - Usuarios del Administrador con su fila propia y filtros Rol y Estado", async ({ page }) => {
    await page.goto(`${APP}/usuarios`);
    await expect(page.getByRole("heading", { level: 1, name: "Usuarios" })).toBeVisible();
    const tabla = page.getByRole("table", { name: "Usuarios de Impuestos" });
    for (const col of ["Nombre", "Correo", "Rol", "Estado", "Acciones"]) await expect(tabla.getByRole("columnheader", { name: col, exact: true })).toBeVisible();
    await expect(tabla.getByText("Eres tú", { exact: true })).toHaveCount(1);

    // Los filtros viven en el estado del componente (spec-visual §3.8 no los lleva a la URL): se comprueba el contenido.
    await page.getByRole("combobox", { name: "Rol" }).selectOption({ label: "Administrador Impuestos" });
    await expect(filasDatos(tabla, page).first()).toBeVisible();
    for (const fila of await filasDatos(tabla, page).all()) await expect(fila.getByRole("cell").nth(2)).toHaveText("Administrador Impuestos");

    await page.getByRole("combobox", { name: "Estado" }).selectOption({ label: "Activo" });
    await expect(filasDatos(tabla, page).first()).toBeVisible();
    for (const fila of await filasDatos(tabla, page).all()) await expect(fila.getByRole("cell").nth(3)).toContainText("Activo");
    await revisarTemas(page, "usuarios-admin", "PR-2");
    await revisar360(page, "usuarios-admin", "PR-2");
  });
});

test.describe("Super Admin de plataforma", () => {
  test.beforeEach(async ({ page }) => {
    exigir(SUPER_ADMIN, "Super Admin");
    exigirEmpresa();
    await entrar(page, SUPER_ADMIN, "/");
  });

  test("QA_TC07_IMPUESTOS_Permisos - Super Admin ve todos los módulos y Usuarios por compañía", async ({ page }) => {
    await esperarDock(page, ["Impuestos", "Reportes", "Usuarios", "Configuración", "Backoffice"]);
    await page.goto(`${APP}/usuarios`);
    const companias = page.getByRole("table", { name: "Compañías con Impuestos" });
    await expect(companias).toBeVisible();
    await expect(companias.getByRole("columnheader", { name: "Usuarios", exact: true })).toBeVisible();
    await expect(companias.getByRole("button", { name: /^Ver usuarios de / }).first()).toBeVisible();
  });

  test("QA_TC08_IMPUESTOS_Permisos - La fila del AdminCompany es protegida y no ofrece acciones", async ({ page }) => {
    await page.goto(`${APP}${conEmpresa("/usuarios", EMPRESA)}`);
    const tabla = page.getByRole("table", { name: "Usuarios de Impuestos" });
    await expect(tabla).toBeVisible();
    const protegida = filasDatos(tabla, page).filter({ hasText: "Este usuario se gestiona desde el administrador de la plataforma." });
    await expect(protegida.first()).toBeVisible();
    await expect(protegida.first().getByRole("button", { name: "Acciones del usuario" })).toHaveCount(0);
    await expect(protegida.first().getByRole("cell").nth(2)).toHaveText("Administrador Impuestos");
  });
});

test.describe("Página 403 (sin sesión)", () => {
  const CASOS: ReadonlyArray<[string, string]> = [
    ["PRODUCTO_NO_HABILITADO", "Impuestos no está activo para tu compañía."],
    ["PRODUCTO_SUSPENDIDO", "Impuestos está suspendido para tu empresa. Pídele ayuda a tu administrador."],
    ["SIN_PERMISOS_IMPUESTOS", "No tienes un rol en Impuestos. Pídele acceso a tu administrador."],
    ["SIN_EMPRESA", "Tu usuario no tiene una compañía asociada."],
    ["CODIGO_DESCONOCIDO", "No tienes permiso para abrir esta página."],
  ];

  test("QA_TC09_IMPUESTOS_Permisos - Literales de la 403 por código y texto general para un código desconocido", async ({ page }) => {
    for (const [codigo, literal] of CASOS) {
      await page.goto(`${APP}/403?code=${codigo}`);
      await expect(page.getByRole("heading", { level: 1, name: "No tienes acceso" })).toBeVisible();
      await expect(page.getByText(literal, { exact: true })).toBeVisible();
      await expect(page.getByText(codigo)).toHaveCount(0);
    }
    await revisarTemas(page, "403", "PR-3");
  });
});
