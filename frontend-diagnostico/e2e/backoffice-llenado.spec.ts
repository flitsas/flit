import { expect, test, type Page } from "@playwright/test";

/**
 * HU #13802 (F06-H5) — backoffice FLIT: elegir compañía, abrir un lote con error, llenar SIMIT vehículo a mano y verlo en
 * verde con la marca «Manual»; un admin de compañía no ve la entrada y la ruta le muestra la 403.
 *
 * Artefacto E2E para QA: necesita la suite levantada (hub en 4022, gateway en 4002, core-diagnostico en 4032 con
 * `Diagnostico__Ambiente=LOCAL|DEV` y `Diagnostico__Simulacion__Habilitada=true`, y esta app en 4024) y un lote de la
 * compañía de prueba con SIMIT vehículo en ERROR (la simulación lo da con el escenario de falla de SIMIT). Excluido de `tsc`
 * y de Vitest; se corre con Playwright desde la raíz del repo, por ejemplo:
 *
 *   DIAGNOSTICO_URL=http://127.0.0.1:4024 E2E_BACKOFFICE_USUARIO=... E2E_BACKOFFICE_CLAVE=... E2E_EMPRESA=<uuid> \
 *   E2E_ADMIN_USUARIO=... E2E_ADMIN_CLAVE=... \
 *   pnpm --dir frontend exec playwright test ../frontend-diagnostico/e2e/backoffice-llenado.spec.ts
 *
 * E2E_BACKOFFICE_USUARIO es un usuario SuperAdmin de la compañía FLIT (tiene todas las capacidades de Diagnóstico, decisión
 * D23); E2E_ADMIN_USUARIO, el admin de una compañía cliente. Las credenciales son de usuarios de
 * prueba del ambiente DEV y entran solo por variables de entorno.
 *
 * Lista congelada (HU #14562 · AC55): nombres accesibles y atributos data-* de los que depende este spec.
 * Si uno cambia por decisión de UX, el spec se ajusta en el mismo PR de la pantalla.
 *   - button «/ingresar|iniciar sesión/i»
 *   - link «Backoffice»
 *   - button «/^Backoffice/» (sección del dock que despliega el submenú)
 *   - heading «Lotes por compañía»
 *   - table «/lotes de la compañía/i»
 *   - row (sin nombre)
 *   - link (sin nombre)
 *   - table «/semáforo de consultas/i»
 *   - link «/^Ver detalle de SIMIT vehículo de .+: Error \(se abre en una pestaña nueva\)$/»
 *   - button «/^Llenar a mano SIMIT vehículo de /»
 *   - dialog «/llenar a mano/i»
 *   - button «Confirmar llenado»
 *   - heading «No tienes acceso»
 *   - etiqueta «/correo|usuario/i»
 *   - etiqueta «Contraseña» (exacta: el hub añadió el botón «Mostrar contraseña»)
 *   - etiqueta «Solo lotes con errores»
 *   - etiqueta «Semáforo»
 *   - texto «Ejecutado»
 *   - texto «Manual»
 *   - texto «Esta compañía no tiene lotes con estos filtros» (estado vacío: el test se omite)
 *   - atributo «data-celda»
 */
const APP = process.env.DIAGNOSTICO_URL ?? "http://127.0.0.1:4024";

async function entrar(page: Page, usuario: string, clave: string) {
  await page.goto(`${APP}/`);
  await page.waitForURL((url) => !url.href.startsWith(APP));
  await page.getByLabel(/correo|usuario/i).fill(usuario);
  await page.getByLabel("Contraseña", { exact: true }).fill(clave);
  await page.getByRole("button", { name: /ingresar|iniciar sesión/i }).click();
  await page.waitForURL(`${APP}/`);
}

test.describe("backoffice: llenado manual", () => {
  test("AC1+AC2: elige la compañía, filtra con errores, llena SIMIT vehículo y la celda queda verde con «Manual»", async ({ page }) => {
    const { E2E_BACKOFFICE_USUARIO: usuario, E2E_BACKOFFICE_CLAVE: clave, E2E_EMPRESA: empresa } = process.env;
    test.skip(!usuario || !clave || !empresa, "Faltan E2E_BACKOFFICE_USUARIO / E2E_BACKOFFICE_CLAVE / E2E_EMPRESA");
    await entrar(page, usuario!, clave!);

    // La entrada del dock existe solo para este perfil. HU #14562: el dock agrupa «Backoffice» como sección (botón que
    // despliega su submenú); el enlace «Backoffice» queda dentro.
    const enlaceBackoffice = page.getByRole("link", { name: "Backoffice", exact: true }).first();
    if (!(await enlaceBackoffice.isVisible())) await page.getByRole("button", { name: /^Backoffice/ }).first().click();
    await enlaceBackoffice.click();
    await page.waitForURL(/\/backoffice\/diagnosticos/);

    // HU #13916 cambió el campo del uuid por el selector «Empresa» (por nombre o NIT); E2E_EMPRESA es el uuid, así que la
    // compañía entra por la URL, que el selector muestra ya elegida (HU #14562 · AC43).
    await page.goto(`${APP}/backoffice/diagnosticos?empresa=${encodeURIComponent(empresa!)}`);
    await expect(page.getByRole("heading", { level: 1, name: "Lotes por compañía" })).toBeVisible();
    await page.getByLabel("Solo lotes con errores").check();
    await expect(page).toHaveURL(/con_errores=true/);

    const lotes = page.getByRole("table", { name: /lotes de la compañía/i });
    // Precondición de datos: sin lotes con error la pantalla muestra el estado vacío (HU #14562) en vez de la tabla.
    const vacio = page.getByText("Esta compañía no tiene lotes con estos filtros");
    await expect(lotes.or(vacio)).toBeVisible();
    test.skip(await vacio.isVisible(), "La compañía de prueba no tiene lotes con errores");
    await expect(lotes).toBeVisible();
    for (const fila of await lotes.getByRole("row").filter({ has: page.getByRole("link") }).all()) await expect(fila).toContainText(/con error/);

    // Abre el primer lote con error y busca un SIMIT vehículo en ERROR.
    await lotes.getByRole("link").first().click();
    await page.waitForURL(/\/backoffice\/diagnosticos\/[0-9a-f-]{36}\?empresa=/i);
    const grilla = page.getByRole("table", { name: /semáforo de consultas/i });
    await expect(grilla).toBeVisible();
    // HU #14087: el semáforo es un enlace a la pestaña del detalle; «Llenar a mano» está en la celda y abre su modal.
    const SEMAFORO_SIMIT_ERROR = /^Ver detalle de SIMIT vehículo de .+: Error \(se abre en una pestaña nueva\)$/;
    const semaforo = grilla.getByRole("link", { name: SEMAFORO_SIMIT_ERROR }).first();
    test.skip((await semaforo.count()) === 0, "El lote elegido no tiene SIMIT vehículo en error");
    await expect(semaforo).toHaveAttribute("target", "_blank");
    await expect(semaforo).toHaveAttribute("href", /\/backoffice\/diagnosticos\/[0-9a-f-]{36}\/vehiculos\/[0-9a-f-]{36}\/consultas\/SIMIT_VEHICULO\?empresa=/i);
    // El locator de `has` se evalúa DENTRO de cada celda: debe partir de `page`, no de `grilla` (la tabla no está dentro del td).
    // Se toma la primera celda SIMIT en error que ofrezca «Llenar a mano» (las BLOQUEADA_POR_RUNT no son llenables, por diseño).
    const candidata = grilla
      .locator("[data-celda]")
      .filter({ has: page.getByRole("link", { name: SEMAFORO_SIMIT_ERROR }) })
      .filter({ has: page.getByRole("button", { name: /^Llenar a mano SIMIT vehículo de / }) })
      .first();
    test.skip((await candidata.count()) === 0, "Ningún SIMIT vehículo en error es llenable (BLOQUEADA_POR_RUNT no se llena a mano)");
    // Se fija por su id: tras el llenado la celda ya no cumple los filtros (deja de estar en error).
    const celda = grilla.locator(`[data-celda="${await candidata.getAttribute("data-celda")}"]`);

    await celda.getByRole("button", { name: /^Llenar a mano SIMIT vehículo de / }).click();
    const form = page.getByRole("dialog", { name: /llenar a mano/i });
    await form.getByLabel("Semáforo").selectOption("VERDE");
    await form.getByRole("button", { name: "Confirmar llenado" }).click();

    await expect(celda).toHaveAttribute("data-estado", "COMPLETADO");
    await expect(celda.getByText("Ejecutado")).toBeVisible();
    await expect(celda.getByText("Manual")).toBeVisible();
  });

  test("AC4: el admin de la compañía no ve el menú de backoffice y la ruta le muestra la 403", async ({ page }) => {
    const { E2E_ADMIN_USUARIO: usuario, E2E_ADMIN_CLAVE: clave } = process.env;
    test.skip(!usuario || !clave, "Faltan E2E_ADMIN_USUARIO / E2E_ADMIN_CLAVE");
    await entrar(page, usuario!, clave!);

    await expect(page.getByRole("link", { name: "Backoffice" })).toHaveCount(0);
    // HU #14562: la sección del dock es un botón; tampoco debe existir para el admin de compañía.
    await expect(page.getByRole("button", { name: /^Backoffice/ })).toHaveCount(0);
    await page.goto(`${APP}/backoffice/diagnosticos`);
    await page.waitForURL(/\/403/);
    await expect(page.getByRole("heading", { level: 1, name: "No tienes acceso" })).toBeVisible();
  });
});
