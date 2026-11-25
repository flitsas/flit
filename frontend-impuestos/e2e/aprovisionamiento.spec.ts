import { expect, test, type Page } from "@playwright/test";
import {
  API,
  APP,
  EMPRESA,
  SUPER_ADMIN,
  alcanzarConTab,
  entrar,
  exigir,
  exigirEmpresa,
  registrarPeticiones,
  responder,
  reescribir,
  revisar360,
  revisarTemas,
  rutaApi,
} from "./comun";

/**
 * HU #14694 · Backoffice de aprovisionamiento del Super Admin (plan §3.1, §5.1, §5.8 M07b; spec-visual §3.10; contrato 1.2.1).
 * Criterios: F1a-CF6 (carga manual como respaldo), F1a-CF10 (estados y acciones), F1b-CF1 (estado visible), F1b-CF3
 * (formulario protegido), F4-CF10 (suspendidas visibles para el SA), delta 6 (columna Usuarios), §5.8 (LISTO_SIN_WEBHOOK →
 * Reintentar; CLAVE_ROTADA en el historial).
 *
 * Modo de la API: el listado y el detalle de E2E_IMP_EMPRESA se leen de la suite real. Los estados que la suite no produce a
 * voluntad (LISTO_SIN_WEBHOOK, REQUIERE_CREDENCIALES, una suspendida, una fila CLAVE_ROTADA) se obtienen con `page.route`
 * reescribiendo la respuesta REAL dentro del contrato; el reintento y la carga de credenciales se simulan (202 / 204 / 422)
 * para no cambiar la llave de la compañía de prueba en el stub. Con E2E_IMP_APROV_REAL=1 y E2E_IMP_EMPRESA_SIN_WEBHOOK (una
 * compañía activada con el escenario del stub `alta-sin-secreto`), el Reintentar va de verdad y se espera «Listo».
 * Datos de las filas simuladas: «Compañía Demo», NIT 900000001.
 *
 * Lista de nombres accesibles (componentes de M18; ajustar en la Fase 3 si cambian):
 *   - h1 «Compañías con Impuestos» · table «Compañías con Impuestos» · columnheader «Compañía», «NIT», «Usuarios», «Estado»,
 *     «Motivo», «Acción» · link «Ver detalle de <Compañía>» · search «Buscar compañías» · textbox «Buscar por nombre o NIT»
 *   - detalle: h1 con el nombre · group «Estado» · button «Reintentar aprovisionamiento» · «Reintento en cola…»
 *   - group «Credenciales» · button «Cargar credenciales» / «Reemplazar» · form «Cargar credenciales» · «API key» (password),
 *     «Secreto del webhook (opcional)» · button «Mostrar API key» · button «Validar y guardar» · «Credenciales guardadas y validadas.»
 *   - table «Historial del aprovisionamiento» · columnheader «Fecha», «De», «A», «Motivo», «Por»
 */
test.describe.configure({ mode: "serial" });

const REAL = process.env.E2E_IMP_APROV_REAL === "1";
const EMPRESA_SIN_WEBHOOK = process.env.E2E_IMP_EMPRESA_SIN_WEBHOOK;

const DEMO = { id: "00000000-0000-4000-8000-0000000000e1", nombre: "Compañía Demo", nit: "900000001" };
const RUTA_EMPRESAS = rutaApi("/backoffice/empresas");
const rutaAprov = (id: string) => rutaApi(`/backoffice/empresas/${id}/aprovisionamiento`);
const rutaReintentar = (id: string) => rutaApi(`/backoffice/empresas/${id}/aprovisionamiento/reintentar`);
const rutaCredenciales = (id: string) => rutaApi(`/backoffice/empresas/${id}/credenciales`);

interface Resumen {
  estado: string;
  motivo: string | null;
  accion: string | null;
  firmaConfirmada: boolean;
}
interface Detalle extends Resumen {
  empresa?: { id: string; nombre: string; nit: string | null } | null;
  modoRespaldoFirma: boolean;
  actualizadoEn: string;
  historial: { desde: string | null; hacia: string; motivo: string | null; actorTipo: string; en: string }[];
}
interface PaginaEmpresas {
  items: { id: string; nombre: string; nit: string | null; aprovisionamiento: Resumen; suspendida: boolean; usuarios?: number }[];
  total: number;
  page: number;
  pageSize: number;
}

const ahora = () => new Date().toISOString();
/** Valor ficticio con la forma de una api_key (43 caracteres); no es una credencial real ni del stub. */
const LLAVE_FICTICIA = "e2efict" + "0".repeat(36);

async function abrirDetalle(page: Page, tenantId: string) {
  await page.goto(`${APP}/backoffice/empresas/${encodeURIComponent(tenantId)}`);
  await expect(page.getByRole("heading", { level: 2, name: "Historial" })).toBeVisible();
}

test.describe("HU #14694 backoffice de aprovisionamiento (Super Admin)", () => {
  test.beforeEach(async ({ page }) => {
    exigir(SUPER_ADMIN, "Super Admin");
    exigirEmpresa();
    await entrar(page, SUPER_ADMIN, "/backoffice/empresas");
  });

  test("QA_TC01_IMPUESTOS_Aprovisionamiento - Listado con columna Usuarios y estado de la compañía de prueba", async ({ page }) => {
    await expect(page.getByRole("heading", { level: 1, name: "Compañías con Impuestos" })).toBeVisible();
    const tabla = page.getByRole("table", { name: "Compañías con Impuestos" });
    await expect(tabla).toBeVisible();
    for (const col of ["Compañía", "NIT", "Usuarios", "Estado", "Motivo", "Acción"]) {
      await expect(tabla.getByRole("columnheader", { name: col, exact: true })).toBeVisible();
    }
    // La compañía de prueba está en la lista real con su estado en texto (nunca solo color).
    const respuesta = await page.request.get(`${API}/backoffice/empresas/${EMPRESA}/aprovisionamiento`);
    expect(respuesta.status(), "GET aprovisionamiento de E2E_IMP_EMPRESA").toBe(200);
    const detalle = (await respuesta.json()) as Detalle;
    const nombre = detalle.empresa?.nombre ?? "";
    expect(nombre, "AprovisionamientoDetalle.empresa.nombre (delta 7)").not.toBe("");
    await page.getByRole("searchbox", { name: "Buscar por nombre o NIT" }).fill(nombre);
    await page.getByRole("button", { name: "Buscar", exact: true }).click();
    const fila = tabla.getByRole("row").filter({ has: page.getByRole("cell", { name: nombre, exact: true }) });
    await expect(fila).toHaveCount(1);
    await expect(fila.getByText(/^(Listo|Listo sin webhook)$/)).toBeVisible();
    await expect(fila.getByRole("cell").nth(2)).toHaveText(/^\d+$/);
    await revisarTemas(page, "backoffice-listado", "AP-1");
    await revisar360(page, "backoffice-listado", "AP-1");
    await alcanzarConTab(page, fila.getByRole("link", { name: `Ver detalle de ${nombre}` }));
  });

  test("QA_TC02_IMPUESTOS_Aprovisionamiento - Compañía suspendida visible con su chip Suspendido", async ({ page }) => {
    // page.route: suma al listado real una fila suspendida (EmpresaBackoffice.suspendida=true con su último estado LISTO).
    await reescribir<PaginaEmpresas>(page, RUTA_EMPRESAS, (real) => ({
      ...real,
      total: real.total + 1,
      items: [
        { id: DEMO.id, nombre: DEMO.nombre, nit: DEMO.nit, aprovisionamiento: { estado: "LISTO", motivo: null, accion: null, firmaConfirmada: true }, suspendida: true, usuarios: 3 },
        ...real.items,
      ],
    }));
    await page.reload();
    const fila = page.getByRole("table", { name: "Compañías con Impuestos" }).getByRole("row").filter({ hasText: DEMO.nombre });
    await expect(fila.getByText("Listo", { exact: true })).toBeVisible();
    await expect(fila.getByText("Suspendido", { exact: true })).toBeVisible();
    await expect(fila.getByRole("cell", { name: "3", exact: true })).toBeVisible();
    await expect(page.getByText("Estás en el backoffice de FLIT: ves todas las compañías, incluidas las suspendidas.")).toBeVisible();
  });

  test("QA_TC03_IMPUESTOS_Aprovisionamiento - Detalle real con estado, credenciales, contrato e historial", async ({ page }) => {
    await abrirDetalle(page, EMPRESA!);
    await expect(page.getByRole("group", { name: "Estado" })).toBeVisible();
    await expect(page.getByRole("group", { name: "Credenciales" })).toBeVisible();
    await expect(page.getByRole("link", { name: "Editar contrato" })).toHaveAttribute("href", new RegExp(`pestana=contractual&empresa=${EMPRESA}`));
    const historial = page.getByRole("table", { name: "Historial del aprovisionamiento" });
    await expect(historial).toBeVisible();
    for (const col of ["Fecha", "De", "A", "Motivo", "Por"]) await expect(historial.getByRole("columnheader", { name: col, exact: true })).toBeVisible();
    // Nunca se muestra el valor de una credencial: solo «Configurada» / «Sin configurar».
    await expect(page.getByRole("group", { name: "Credenciales" }).getByText(/^API key: (Configurada|Sin configurar)/)).toBeVisible();
    await revisarTemas(page, "backoffice-detalle", "AP-2");
    await revisar360(page, "backoffice-detalle", "AP-2");
  });

  test("QA_TC04_IMPUESTOS_Aprovisionamiento - Reintentar en Listo sin webhook pide el secreto con el alta", async ({ page }) => {
    const objetivo = REAL && EMPRESA_SIN_WEBHOOK ? EMPRESA_SIN_WEBHOOK : EMPRESA!;
    const reintentos = registrarPeticiones(page, rutaReintentar(objetivo));
    if (!(REAL && EMPRESA_SIN_WEBHOOK)) {
      // page.route: estado LISTO_SIN_WEBHOOK con accion REINTENTAR (contrato 1.2.0, §5.8 punto 5) y 202 al reintentar.
      await reescribir<Detalle>(page, rutaAprov(objetivo), (real) => ({ ...real, estado: "LISTO_SIN_WEBHOOK", motivo: "SIN_WEBHOOK_SECRET", accion: "REINTENTAR", firmaConfirmada: false }));
      await responder(page, rutaReintentar(objetivo), 202, undefined, "POST");
    }
    await abrirDetalle(page, objetivo);
    const estado = page.getByRole("group", { name: "Estado" });
    await expect(estado.getByText("Listo sin webhook", { exact: true })).toBeVisible();
    await expect(estado.getByText("Falta el secreto del webhook: los resultados se consultan al proveedor.")).toBeVisible();
    await estado.getByRole("button", { name: "Reintentar aprovisionamiento" }).click();
    await expect(page.getByText("Reintento en cola. El estado se actualiza en unos segundos.")).toBeVisible();
    expect(reintentos.length, "POST …/aprovisionamiento/reintentar").toBe(1);
    if (REAL && EMPRESA_SIN_WEBHOOK) {
      // Reintentar pasa a PENDIENTE y la siguiente ronda (~10 s) llama al alta, que genera el secreto (stub alta-sin-secreto).
      await expect(async () => {
        await page.reload();
        await expect(page.getByRole("group", { name: "Estado" }).getByText("Listo", { exact: true })).toBeVisible({ timeout: 2_000 });
      }).toPass({ timeout: 90_000, intervals: [5_000] });
    }
  });

  test("QA_TC05_IMPUESTOS_Aprovisionamiento - Reintentar en un estado no reintentable muestra el literal del contrato", async ({ page }) => {
    // page.route: estado INCIERTO (reintentable en la UI) y la API responde 409 ESTADO_NO_REINTENTABLE (carrera con otra ronda).
    await reescribir<Detalle>(page, rutaAprov(EMPRESA!), (real) => ({ ...real, estado: "INCIERTO", motivo: "ALTA_SIN_CONFIRMAR", accion: "REINTENTAR" }));
    await responder(page, rutaReintentar(EMPRESA!), 409, { code: "ESTADO_NO_REINTENTABLE", error: "En el estado actual de la compañía no se puede reintentar." }, "POST");
    await abrirDetalle(page, EMPRESA!);
    await page.getByRole("button", { name: "Reintentar aprovisionamiento" }).click();
    await expect(page.getByText("No se pudo reintentar")).toBeVisible();
    await expect(page.getByText("En el estado actual de la compañía no se puede reintentar.")).toBeVisible();
  });

  test("QA_TC06_IMPUESTOS_Aprovisionamiento - Carga manual de credenciales como respaldo en Requiere credenciales", async ({ page }) => {
    // page.route: REQUIERE_CREDENCIALES sin llave guardada; el PUT se simula (204) para no reemplazar la llave real del stub.
    await reescribir<Detalle>(page, rutaAprov(EMPRESA!), (real) => ({ ...real, estado: "REQUIERE_CREDENCIALES", motivo: "CREDENCIAL_RECHAZADA", accion: "CARGAR_CREDENCIALES" }));
    // «Configurada» depende de que ya ocurrió el PUT, no del número de GET (en dev, StrictMode lee dos veces al montar).
    let guardada = false;
    await page.route(rutaCredenciales(EMPRESA!), async (route) => {
      const metodo = route.request().method();
      if (metodo === "PUT") {
        const cuerpo = route.request().postDataJSON() as { apiKey?: string | null; webhookSecret?: string | null };
        expect(cuerpo.apiKey, "PUT credenciales: apiKey").toBe(LLAVE_FICTICIA);
        guardada = true;
        return route.fulfill({ status: 204, body: "" });
      }
      const configurada = guardada;
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ tieneApiKey: configurada, tieneWebhookSecret: false, origen: configurada ? "CARGA_MANUAL" : null, apiKeyPrefijo: configurada ? "e2efict" : null, actualizadoEn: configurada ? ahora() : null }),
      });
    });
    await abrirDetalle(page, EMPRESA!);
    const grupo = page.getByRole("group", { name: "Credenciales" });
    await expect(grupo.getByText("API key: Sin configurar")).toBeVisible();
    await grupo.getByRole("button", { name: "Cargar credenciales" }).click();
    const form = page.getByRole("form", { name: "Cargar credenciales" });
    const llave = form.getByLabel("API key", { exact: true });
    await expect(llave).toHaveAttribute("type", "password");
    await llave.fill(LLAVE_FICTICIA);
    await form.getByRole("button", { name: "Mostrar API key" }).click();
    await expect(llave).toHaveAttribute("type", "text");
    await form.getByRole("button", { name: "Validar y guardar" }).click();
    await expect(page.getByText("Credenciales guardadas y validadas.")).toBeVisible();
    // Nunca se vuelve a mostrar el valor: el formulario se cierra y el resumen dice «Configurada».
    await expect(page.getByRole("form", { name: "Cargar credenciales" })).toHaveCount(0);
    await expect(grupo.getByText(/^API key: Configurada/)).toBeVisible();
    await expect(page.getByText(LLAVE_FICTICIA)).toHaveCount(0);
  });

  test("QA_TC07_IMPUESTOS_Aprovisionamiento - Credencial rechazada por el proveedor muestra CREDENCIAL_INVALIDA", async ({ page }) => {
    await reescribir<Detalle>(page, rutaAprov(EMPRESA!), (real) => ({ ...real, estado: "REQUIERE_CREDENCIALES", motivo: "CREDENCIAL_RECHAZADA", accion: "CARGAR_CREDENCIALES" }));
    await page.route(rutaCredenciales(EMPRESA!), async (route) => {
      if (route.request().method() === "PUT")
        return route.fulfill({ status: 422, contentType: "application/json", body: JSON.stringify({ code: "CREDENCIAL_INVALIDA", error: "El proveedor rechazó la API key: revisa el valor e intenta de nuevo." }) });
      return route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ tieneApiKey: false, tieneWebhookSecret: false, origen: null, apiKeyPrefijo: null, actualizadoEn: null }) });
    });
    await abrirDetalle(page, EMPRESA!);
    await page.getByRole("group", { name: "Credenciales" }).getByRole("button", { name: "Cargar credenciales" }).click();
    const form = page.getByRole("form", { name: "Cargar credenciales" });
    await form.getByLabel("API key", { exact: true }).fill(LLAVE_FICTICIA);
    await form.getByRole("button", { name: "Validar y guardar" }).click();
    await expect(page.getByText("No se guardaron las credenciales")).toBeVisible();
    await expect(page.getByText("El proveedor rechazó la API key: revisa el valor e intenta de nuevo.").first()).toBeVisible();
  });

  test("QA_TC08_IMPUESTOS_Aprovisionamiento - Historial con la fila CLAVE_ROTADA del alta idempotente", async ({ page }) => {
    // page.route: suma al historial real la fila aparte de CLAVE_ROTADA (desde = hacia = LISTO, §5.8 notas de M07b).
    await reescribir<Detalle>(page, rutaAprov(EMPRESA!), (real) => ({
      ...real,
      estado: "LISTO",
      historial: [{ desde: "LISTO", hacia: "LISTO", motivo: "CLAVE_ROTADA", actorTipo: "SISTEMA", en: ahora() }, ...real.historial],
    }));
    await abrirDetalle(page, EMPRESA!);
    const fila = page.getByRole("table", { name: "Historial del aprovisionamiento" }).getByRole("row").filter({ hasText: "El proveedor reemplazó la API key de la compañía; la nueva quedó guardada." });
    await expect(fila).toHaveCount(1);
    await expect(fila.getByRole("cell", { name: "Listo", exact: true })).toHaveCount(2);
    await expect(fila.getByRole("cell", { name: "Sistema", exact: true })).toBeVisible();
    // LISTO no es reintentable: no hay botón.
    await expect(page.getByRole("button", { name: "Reintentar aprovisionamiento" })).toHaveCount(0);
  });
});
