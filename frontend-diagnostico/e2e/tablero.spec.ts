import { expect, test, type Page } from "@playwright/test";

/**
 * HU #13748 (F04-H2) — tablero del lote con semáforos y sondeo incremental.
 *
 * Artefacto E2E para QA: necesita la suite levantada (hub en 4022, gateway en 4002, core-diagnostico en 4029 con
 * `Diagnostico__Ambiente=LOCAL|DEV` y `Diagnostico__Simulacion__Habilitada=true`, y esta app en 4024). Con la simulación de
 * RUNT (HU #13725) el escenario sale del último carácter del identificador: terminado en 1 = persona natural, consulta
 * exitosa. Excluido de `tsc` y de Vitest; se corre con Playwright desde la raíz del repo, por ejemplo:
 *
 *   DIAGNOSTICO_URL=http://127.0.0.1:4024 E2E_USUARIO=... E2E_CLAVE=... \
 *   pnpm --dir frontend exec playwright test ../frontend-diagnostico/e2e/tablero.spec.ts
 *
 * E2E_USUARIO es un usuario de la compañía con diagnosticos.crear y diagnosticos.ver, SIN rol admin (sin
 * reprocesarConsultas). Las credenciales son de usuarios de prueba del ambiente DEV y entran solo por variables de entorno.
 */
const APP = process.env.DIAGNOSTICO_URL ?? "http://127.0.0.1:4024";
/** Placa de prueba terminada en 1: la simulación de RUNT responde con éxito. */
const PLACA = process.env.E2E_PLACA_EXPRESS ?? "TST101";

async function entrar(page: Page, usuario: string, clave: string) {
  await page.goto(`${APP}/`);
  await page.waitForURL((url) => !url.href.startsWith(APP));
  await page.getByLabel(/correo|usuario/i).fill(usuario);
  await page.getByLabel(/contraseña/i).fill(clave);
  await page.getByRole("button", { name: /ingresar|iniciar sesión/i }).click();
  await page.waitForURL(`${APP}/`);
  await expect(page.getByRole("heading", { level: 1, name: "Diagnóstico vehicular" })).toBeVisible();
}

test.describe("tablero del lote", () => {
  test("un Express pasa de gris a verde sin recargar y el rol usuario no ve botones", async ({ page }) => {
    test.skip(!process.env.E2E_USUARIO || !process.env.E2E_CLAVE, "Faltan E2E_USUARIO / E2E_CLAVE");
    await entrar(page, process.env.E2E_USUARIO!, process.env.E2E_CLAVE!);

    // Lanza el Express: la app navega a /diagnosticos/{id} (antes de esta HU terminaba en not-found).
    await page.getByLabel(/placa o vin/i).fill(PLACA);
    await page.getByRole("button", { name: "Diagnosticar" }).click();
    await page.getByRole("button", { name: "Lanzar diagnóstico" }).click();
    await page.waitForURL(/\/diagnosticos\/[0-9a-f-]{36}$/i);

    const grilla = page.getByRole("table", { name: /semáforo de consultas/i });
    await expect(grilla).toBeVisible();
    const fila = grilla.getByRole("row").filter({ has: page.getByRole("rowheader", { name: new RegExp(PLACA) }) });
    const runt = fila.locator('[data-celda][data-estado]').first();

    // AC2: sin recargar, el sondeo de 3 s lleva la celda RUNT de gris (PENDIENTE/EN_CURSO) a verde (COMPLETADO).
    let recargas = 0;
    page.on("framenavigated", (frame) => {
      if (frame === page.mainFrame()) recargas++;
    });
    await expect(runt).toHaveAttribute("data-estado", "COMPLETADO", { timeout: 30_000 });
    await expect(runt.getByText("Completada")).toBeVisible();
    expect(recargas).toBe(0);

    // AC4: el usuario sin rol admin no ve ningún botón de acción en la grilla (desde HU #13801 cada semáforo es un botón
    // «Ver detalle de …», que no es una acción sobre la celda).
    await expect(grilla.getByRole("button", { name: /reprocesar/i })).toHaveCount(0);
    // AC6: ningún indicador de dato manual.
    await expect(grilla.getByText(/manual/i)).toHaveCount(0);
  });

  test("HU #13801 AC1: clic en el semáforo verde de SIMIT vehículo abre el detalle con sus comparendos", async ({ page }) => {
    test.skip(!process.env.E2E_USUARIO || !process.env.E2E_CLAVE, "Faltan E2E_USUARIO / E2E_CLAVE");
    await entrar(page, process.env.E2E_USUARIO!, process.env.E2E_CLAVE!);

    await page.getByLabel(/placa o vin/i).fill(PLACA);
    await page.getByRole("button", { name: "Diagnosticar" }).click();
    await page.getByRole("button", { name: "Lanzar diagnóstico" }).click();
    await page.waitForURL(/\/diagnosticos\/[0-9a-f-]{36}$/i);

    const grilla = page.getByRole("table", { name: /semáforo de consultas/i });
    await expect(grilla).toBeVisible();
    test.skip((await grilla.getByRole("columnheader", { name: "SIMIT vehículo" }).count()) === 0, "El Express de este ambiente no incluye SIMIT vehículo");

    // Con la simulación encendida, SIMIT del vehículo termina en verde (COMPLETADO) sin recargar.
    const semaforo = grilla.getByRole("button", { name: new RegExp(`^Ver detalle de SIMIT vehículo de ${PLACA}`) });
    await expect(semaforo).toHaveAccessibleName(/Completada$/, { timeout: 60_000 });

    // Se abre con el teclado: Enter sobre el semáforo enfocado.
    await semaforo.focus();
    await page.keyboard.press("Enter");
    const dialogo = page.getByRole("dialog", { name: `SIMIT vehículo · ${PLACA}` });
    await expect(dialogo).toBeVisible();
    await expect(dialogo.getByText(/^Dato del \d{2}\/\d{2}\/\d{4} \d{2}:\d{2}$/)).toBeVisible();
    await expect(dialogo.getByText(/^\d+ comparendos?$/)).toBeVisible();
    // Comparendos con valor y estado, o el vacío explícito si la simulación no trae ninguno.
    const tabla = dialogo.getByRole("table", { name: /comparendos/i });
    if (await tabla.count()) await expect(tabla.getByRole("columnheader", { name: "Valor" })).toBeVisible();
    else await expect(dialogo.getByText("Sin comparendos registrados.")).toBeVisible();
    // AC5: nada que delate un dato manual.
    await expect(dialogo.getByText(/manual/i)).toHaveCount(0);

    // Escape cierra y el foco vuelve al semáforo.
    await page.keyboard.press("Escape");
    await expect(dialogo).toHaveCount(0);
    await expect(semaforo).toBeFocused();
  });
});
