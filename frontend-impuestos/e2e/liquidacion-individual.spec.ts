import { expect, test, type Page } from "@playwright/test";
import {
  ADMIN,
  APP,
  EMPRESA,
  SUPER_ADMIN,
  alcanzarConTab,
  conEmpresa,
  entrar,
  escenarioStub,
  esperarChip,
  exigir,
  exigirEmpresa,
  exigirStub,
  idsDeUrl,
  liquidarPlaca,
  placaFicticia,
  registrarPeticiones,
  reescribir,
  responder,
  restaurarStub,
  revisar360,
  revisarTemas,
  rutaApi,
  type Sesion,
} from "./comun";

/**
 * HU #14694 / #14695 · Liquidación por placa (plan §2.2, §3.2, §5.1, §6.1; spec-visual §3.3 y §3.6; contrato 1.2.1).
 * Criterios: F2a-CF1 (solo placa), F1b-CF4 (sin LISTO no se liquida: APROVISIONAMIENTO_NO_LISTO), F2b-CF7 (null ≠ 0: «Sin dato»,
 * warnings, error clasificado), delta 2 (?empresa del Super Admin: «Elige una compañía.»), §3.2 (relanzar con aviso de
 * duplicado; contrato 1.2.0 «Ver la original»).
 *
 * Modo de la API: suite real + stub 8898. Resultado del stub por defecto (`consulta-ok-deuda`): 2025 y 2024 con recibo,
 * 2022 `period_not_liquidable` con todos los montos null, `previousPayments` null en todas; total 1.779.650. PAR000 → parcial.
 * Para el relanzamiento se cambia el escenario por defecto a `consulta-failed-captcha_blocked` (clase REINTENTABLE, sin
 * reintento automático) y se restaura en afterEach. `page.route` solo en TC04/TC05: sesión con aprovisionamiento no listo y
 * 409 APROVISIONAMIENTO_NO_LISTO del POST (la compañía de prueba está LISTO).
 *
 * Lista de nombres accesibles (componentes de M16; ajustar en la Fase 3 si cambian):
 *   - form «Liquidar placa» · textbox «Placa» · button «Liquidar placa» · «Escribe una placa válida: de 4 a 10 letras o números.»
 *   - detalle: h1 con la placa · chip «Con deuda» / «Con error» · marca «Parcial» / «Posible duplicado» · button «Relanzar»
 *   - region «Vehículo y propietario», «Totales» («Total a pagar», «Vigencias pendientes»), «Vigencias» (table «Vigencias»,
 *     columnheader «Pagos previos», «Total a pagar»), «Recibos»
 *   - dialog «Relanzar la placa <placa>» · checkbox «Entiendo que puede quedar duplicada» · button «Relanzar» · link «Ver la original»
 *   - EstadoVacio heading «Elige una compañía.»
 */
test.describe.configure({ mode: "serial" });

const RUTA_INDIVIDUAL = rutaApi("/liquidaciones/individual");

async function abrirNueva(page: Page, empresa?: string | null) {
  await page.goto(`${APP}${conEmpresa("/liquidaciones/nueva", empresa)}`);
  return page.getByRole("form", { name: "Liquidar placa" });
}

test.describe("HU #14694 liquidación por placa (Administrador)", () => {
  test.beforeEach(async ({ page, request }) => {
    exigir(ADMIN, "Administrador");
    await exigirStub(request);
    await entrar(page, ADMIN, "/liquidaciones");
  });
  test.afterEach(async ({ request }) => {
    await restaurarStub(request);
  });

  test("QA_TC01_IMPUESTOS_LiquidacionIndividual - Happy path placa con deuda, vigencias y totales", async ({ page }) => {
    const placa = placaFicticia();
    await liquidarPlaca(page, placa);
    await esperarChip(page, "Con deuda");

    const totales = page.getByRole("region", { name: "Totales" });
    await expect(totales).toContainText(/1\.779\.650/);
    await expect(totales).toContainText("Vigencias pendientes");

    const vigencias = page.getByRole("table", { name: "Vigencias" });
    for (const col of ["Vigencia", "Avalúo", "Impuesto", "Pagos previos", "Total a pagar", "Vencimiento"]) {
      await expect(vigencias.getByRole("columnheader", { name: col, exact: true })).toBeVisible();
    }
    // null ≠ 0: la vigencia 2022 no liquidable dice «Sin dato» en sus montos y lleva la marca del portal; nunca «$ 0».
    const fila2022 = vigencias.getByRole("row").filter({ hasText: "2022" }).first();
    await expect(fila2022.getByText("El portal no permite liquidar esta vigencia")).toBeVisible();
    expect(await fila2022.getByText("Sin dato", { exact: true }).count()).toBeGreaterThanOrEqual(5);
    await expect(fila2022).not.toContainText("$ 0");
    // previousPayments null también en una vigencia liquidable.
    const fila2025 = vigencias.getByRole("row").filter({ hasText: "2025" }).first();
    await expect(fila2025.getByText("Sin dato", { exact: true }).first()).toBeVisible();

    await expect(page.getByRole("region", { name: "Vehículo y propietario" })).toBeVisible();
    await expect(page.getByRole("region", { name: "Recibos" })).toBeVisible();
    await revisarTemas(page, "detalle-placa", "LI-1");
    await revisar360(page, "detalle-placa", "LI-1");
  });

  test("QA_TC02_IMPUESTOS_LiquidacionIndividual - Placa inválida no llama a la API", async ({ page }) => {
    const envios = registrarPeticiones(page, RUTA_INDIVIDUAL);
    const form = await abrirNueva(page);
    const campo = form.getByRole("textbox", { name: "Placa" });
    await expect(campo).toBeFocused();
    await campo.fill("AB");
    await campo.press("Enter");
    await expect(form.getByText("Escribe una placa válida: de 4 a 10 letras o números.")).toBeVisible();
    await expect(campo).toHaveAttribute("aria-invalid", "true");
    expect(envios.length, "POST /liquidaciones/individual con placa inválida").toBe(0);
  });

  test("QA_TC03_IMPUESTOS_LiquidacionIndividual - Resultado parcial con su marca y aviso", async ({ page }) => {
    await liquidarPlaca(page, "PAR000");
    await esperarChip(page, "Con deuda");
    await expect(page.getByText("Parcial", { exact: true }).first()).toBeVisible();
    await expect(page.getByText("Resultado parcial: el proveedor no pudo liquidar todas las vigencias.")).toBeVisible();
  });

  test("QA_TC04_IMPUESTOS_LiquidacionIndividual - Aprovisionamiento no listo deshabilita Liquidar placa", async ({ page }) => {
    // page.route: la sesión real con el aprovisionamiento en REQUIERE_CREDENCIALES (banner NO_LISTO).
    await reescribir<Sesion>(page, rutaApi("/sesion"), (real) => ({ ...real, aprovisionamiento: { estado: "REQUIERE_CREDENCIALES", firmaConfirmada: false } }));
    const form = await abrirNueva(page);
    await expect(form.getByRole("button", { name: "Liquidar placa" })).toBeDisabled();
    await expect(form.getByText("La compañía todavía no puede liquidar porque su configuración con el proveedor no está lista.")).toBeVisible();
  });

  test("QA_TC05_IMPUESTOS_LiquidacionIndividual - 409 APROVISIONAMIENTO_NO_LISTO del servidor se explica en línea", async ({ page }) => {
    // page.route: carrera entre la sesión (LISTO) y el servidor, que ya no lo está (409 del contrato).
    await responder(page, RUTA_INDIVIDUAL, 409, { code: "APROVISIONAMIENTO_NO_LISTO", error: "La compañía todavía no puede liquidar porque su configuración con el proveedor no está lista." }, "POST");
    const form = await abrirNueva(page);
    await form.getByRole("textbox", { name: "Placa" }).fill(placaFicticia());
    await form.getByRole("button", { name: "Liquidar placa" }).click();
    await expect(page.getByText("No se pudo liquidar la placa")).toBeVisible();
    await expect(page.getByText("La compañía todavía no puede liquidar porque su configuración con el proveedor no está lista.")).toBeVisible();
    await expect(page).toHaveURL(/\/liquidaciones\/nueva/);
  });

  test("QA_TC06_IMPUESTOS_LiquidacionIndividual - Relanzar con confirmación y enlace Ver la original", async ({ page, request }) => {
    await escenarioStub(request, "consulta", "consulta-failed-captcha_blocked");
    const placa = placaFicticia();
    const original = await liquidarPlaca(page, placa);
    const urlOriginal = new URL(page.url()).pathname;
    await esperarChip(page, "Con error");
    await expect(page.getByText("Reintentable como nueva consulta")).toBeVisible();

    await page.getByRole("button", { name: "Relanzar", exact: true }).click();
    const dialogo = page.getByRole("dialog", { name: `Relanzar la placa ${placa}` });
    await expect(dialogo).toBeVisible();
    await expect(dialogo.getByText("Relanzar crea una liquidación nueva para la placa. El proveedor puede haber recibido la solicitud anterior: podría quedar duplicada.")).toBeVisible();
    // Foco inicial en Cancelar; Relanzar deshabilitado hasta aceptar el posible duplicado (422 CONFIRMACION_REQUERIDA si no).
    await expect(dialogo.getByRole("button", { name: "Cancelar" })).toBeFocused();
    const confirmar = dialogo.getByRole("button", { name: "Relanzar", exact: true });
    await expect(confirmar).toBeDisabled();
    await expect(dialogo.getByText("Confirma que aceptas un posible duplicado en el proveedor.")).toBeVisible();
    await dialogo.getByRole("checkbox", { name: "Entiendo que puede quedar duplicada" }).check();
    await expect(confirmar).toBeEnabled();
    await revisarTemas(page, "dialogo-relanzar", "LI-2");
    await confirmar.click();

    await page.waitForURL((url) => /\/items\//.test(url.pathname) && url.pathname !== urlOriginal);
    const nueva = idsDeUrl(page.url());
    expect(nueva.liquidacionId, "el relanzamiento crea otra liquidación individual").not.toBe(original.liquidacionId);
    await expect(page.getByText("Posible duplicado", { exact: true }).first()).toBeVisible();
    await expect(page.getByText("Esta liquidación es un relanzamiento: puede estar duplicada en el proveedor.")).toBeVisible();
    const verOriginal = page.getByRole("link", { name: "Ver la original" });
    await alcanzarConTab(page, verOriginal);
    await page.keyboard.press("Enter");
    await page.waitForURL((url) => url.pathname === urlOriginal);
    await expect(page.getByRole("heading", { level: 1, name: placa })).toBeVisible();
  });
});

test.describe("HU #14694 liquidación por placa (Super Admin y ?empresa)", () => {
  test.beforeEach(async ({ page, request }) => {
    exigir(SUPER_ADMIN, "Super Admin");
    exigirEmpresa();
    await exigirStub(request);
    await entrar(page, SUPER_ADMIN, "/liquidaciones");
  });

  test("QA_TC07_IMPUESTOS_LiquidacionIndividual - Super Admin sin compañía ve Elige una compañía y no puede liquidar", async ({ page }) => {
    const envios = registrarPeticiones(page, RUTA_INDIVIDUAL);
    const form = await abrirNueva(page, null);
    await expect(form.getByText("Elige una compañía.")).toBeVisible();
    await expect(form.getByRole("button", { name: "Liquidar placa" })).toBeDisabled();
    expect(envios.length).toBe(0);
  });

  test("QA_TC08_IMPUESTOS_LiquidacionIndividual - Super Admin liquida con ?empresa y el detalle exige la compañía", async ({ page }) => {
    const envios = registrarPeticiones(page, RUTA_INDIVIDUAL);
    const placa = placaFicticia();
    const { liquidacionId, itemId } = await liquidarPlaca(page, placa, EMPRESA);
    expect(envios.at(-1)?.searchParams.get("empresa"), "POST individual con ?empresa (delta 2)").toBe(EMPRESA);
    // El detalle muestra la compañía en la descripción para el Super Admin.
    await expect(page).toHaveURL(new RegExp(`empresa=${EMPRESA}`));
    // Sin ?empresa no se pide nada a la API: «Elige una compañía.» (literal EMPRESA_REQUERIDA).
    await page.goto(`${APP}/liquidaciones/${liquidacionId}/items/${itemId}`);
    await expect(page.getByRole("heading", { name: "Elige una compañía." })).toBeVisible();
    await expect(page.getByRole("heading", { level: 1, name: placa })).toHaveCount(0);
  });
});
