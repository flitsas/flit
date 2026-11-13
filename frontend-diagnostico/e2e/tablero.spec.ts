import { expect, test, type Page } from "@playwright/test";
import { lanzarExpress } from "./express";

/**
 * HU #13748 (F04-H2) — tablero del lote con semáforos y sondeo incremental.
 *
 * Artefacto E2E para QA: necesita la suite levantada (hub en 4022, gateway en 4002, core-diagnostico en 4032 con
 * `Diagnostico__Ambiente=LOCAL|DEV` y `Diagnostico__Simulacion__Habilitada=true`, y esta app en 4024). Con la simulación de
 * RUNT (HU #13725) el escenario sale del último carácter del identificador: terminado en 1 = persona natural, consulta
 * exitosa. Excluido de `tsc` y de Vitest; se corre con Playwright desde la raíz del repo, por ejemplo:
 *
 *   DIAGNOSTICO_URL=http://127.0.0.1:4024 E2E_USUARIO=... E2E_CLAVE=... \
 *   pnpm --dir frontend exec playwright test ../frontend-diagnostico/e2e/tablero.spec.ts
 *
 * E2E_USUARIO es un usuario de la compañía con diagnosticos.crear y diagnosticos.ver, SIN rol admin (sin
 * reprocesarConsultas). Las credenciales son de usuarios de prueba del ambiente DEV y entran solo por variables de entorno.
 *
 * Lista congelada (HU #14562 · AC55): nombres accesibles y atributos data-* de los que depende este spec.
 * Si uno cambia por decisión de UX, el spec se ajusta en el mismo PR de la pantalla.
 *   - button «/ingresar|iniciar sesión/i»
 *   - heading «Diagnóstico vehicular»
 *   - table «/semáforo de consultas/i»
 *   - row (sin nombre)
 *   - rowheader «new RegExp(PLACA)»
 *   - button «/reprocesar/i»
 *   - button «/^Vehículos: \d+$/»
 *   - columnheader «Dictamen»
 *   - button «Ver dictamen de ${PLACA}»
 *   - dialog «Dictamen · ${PLACA}»
 *   - button «Cerrar»
 *   - dialog (sin nombre)
 *   - link «new RegExp(`^Ver detalle de SIMIT vehículo de ${PLACA}`)»
 *   - heading «SIMIT vehículo · ${PLACA}»
 *   - navigation «Migas de pan»
 *   - link «Diagnósticos»
 *   - listitem (sin nombre)
 *   - region «Auditoría de consulta»
 *   - region «Respuesta de la fuente»
 *   - group «Respuesta de la fuente»
 *   - term (sin nombre)
 *   - table «Comparendos»
 *   - columnheader «Valor»
 *   - region «/^resultado$/i»
 *   - button «Respuesta del proveedor»
 *   - link «Volver al diagnóstico»
 *   - link «Volver a diagnósticos»
 *   - etiqueta «/correo|usuario/i»
 *   - etiqueta «/contraseña/i»
 *   - texto «Ejecutado»
 *   - texto «/manual/i»
 *   - texto «SIMIT vehículo»
 *   - texto «/^\d{2}\/\d{2}\/\d{4} \d{2}:\d{2}$/»
 *   - texto «/no tiene guardada la respuesta completa/»
 *   - texto «Sin registros»
 *   - texto «/proveedor|desde_cache|http_status|duracion_ms|semaforo/i»
 *   - texto «/^Cantidad de comparendos$/»
 *   - texto «/^Comparendos$/»
 *   - atributo «data-celda»
 *   - atributo «data-estado»
 *   Del helper express.ts (lanzarExpress):
 *     - textbox «Placa o VIN»
 *     - button «Diagnosticar en línea»
 *     - textbox «Nombre del diagnóstico»
 *     - button «/^(Continuar|Iniciar diagnóstico)$/»
 *     - button «Lanzar diagnóstico»
 *     - link «Ver tablero»
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
    // HU #14085: el inicio abre la pantalla única en Express con la placa precargada.
    await lanzarExpress(page, PLACA);

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
    await expect(runt.getByText("Ejecutado")).toBeVisible();
    expect(recargas).toBe(0);

    // AC4: el usuario sin rol admin no ve ningún botón de acción en la grilla (desde HU #14087 cada semáforo es un enlace
    // «Ver detalle de …» a la pestaña del detalle, que no es una acción sobre la celda).
    await expect(grilla.getByRole("button", { name: /reprocesar/i })).toHaveCount(0);
    // AC6: ningún indicador de dato manual.
    await expect(grilla.getByText(/manual/i)).toHaveCount(0);

    // HU #14562 AC27/AC28: tarjeta-filtro «Vehículos» y columna Dictamen; el dictamen abre un panel lateral con «Cerrar».
    await expect(page.getByRole("button", { name: /^Vehículos: \d+$/ })).toBeVisible();
    await expect(grilla.getByRole("columnheader", { name: "Dictamen" })).toBeVisible();
    const dictamen = grilla.getByRole("button", { name: `Ver dictamen de ${PLACA}` });
    await expect(dictamen).toHaveText(/Viable|Bloqueado|Pendiente/);
    await dictamen.click();
    const panel = page.getByRole("dialog", { name: `Dictamen · ${PLACA}` });
    await expect(panel).toBeVisible();
    await panel.getByRole("button", { name: "Cerrar" }).click();
    await expect(page.getByRole("dialog")).toHaveCount(0);
  });

  test("HU #13801 AC1 / HU #14087: el semáforo verde de SIMIT vehículo abre el detalle en una pestaña nueva", async ({ page, context }) => {
    test.skip(!process.env.E2E_USUARIO || !process.env.E2E_CLAVE, "Faltan E2E_USUARIO / E2E_CLAVE");
    await entrar(page, process.env.E2E_USUARIO!, process.env.E2E_CLAVE!);

    // HU #14085: el inicio abre la pantalla única en Express con la placa precargada.
    await lanzarExpress(page, PLACA);

    const grilla = page.getByRole("table", { name: /semáforo de consultas/i });
    await expect(grilla).toBeVisible();
    // HU #14087: las 8 fuentes salen siempre; si el Express no pidió SIMIT vehículo su celda dice «Sin solicitar» y no es enlace.
    test.skip(
      (await grilla.getByRole("link", { name: new RegExp(`^Ver detalle de SIMIT vehículo de ${PLACA}`) }).count()) === 0,
      "El Express de este ambiente no incluye SIMIT vehículo",
    );

    // Con la simulación encendida, SIMIT del vehículo termina en verde (COMPLETADO) sin recargar.
    const semaforo = grilla.getByRole("link", { name: new RegExp(`^Ver detalle de SIMIT vehículo de ${PLACA}`) });
    await expect(semaforo).toHaveAccessibleName(/Ejecutado \(se abre en una pestaña nueva\)$/, { timeout: 60_000 });
    await expect(semaforo).toHaveAttribute("target", "_blank");
    await expect(semaforo).toHaveAttribute("rel", "noopener noreferrer");

    // Se abre con el teclado (Enter sobre el semáforo enfocado) en una pestaña nueva; el tablero sigue en la suya, sin modal.
    await semaforo.focus();
    const [pestana] = await Promise.all([context.waitForEvent("page"), page.keyboard.press("Enter")]);
    await pestana.waitForLoadState();
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await expect(pestana).toHaveURL(/\/diagnosticos\/[0-9a-f-]{36}\/vehiculos\/[0-9a-f-]{36}\/consultas\/SIMIT_VEHICULO$/i);
    await expect(pestana.getByRole("heading", { level: 1, name: `SIMIT vehículo · ${PLACA}` })).toBeVisible();
    const migas = pestana.getByRole("navigation", { name: "Migas de pan" });
    await expect(migas.getByRole("link", { name: "Diagnósticos" })).toBeVisible();
    // HU #14562 · AC39: miga de 4 niveles «Diagnósticos › lote › placa › fuente».
    await expect(migas.getByRole("listitem")).toHaveCount(4);
    await expect(migas.getByText("SIMIT vehículo", { exact: true })).toHaveAttribute("aria-current", "page");
    // Auditoría de consulta de FLIT: fecha y hora de la consulta y el estado.
    const auditoria = pestana.getByRole("region", { name: "Auditoría de consulta" });
    await expect(auditoria.getByText(/^\d{2}\/\d{2}\/\d{4} \d{2}:\d{2}$/)).toBeVisible();
    await expect(auditoria.getByText("Ejecutado")).toBeVisible();
    // Respuesta de la fuente: solo datos de la consulta, en español; sin la auditoría del proveedor ni nombres crudos.
    const seccionRespuesta = pestana.getByRole("region", { name: "Respuesta de la fuente" });
    if (await seccionRespuesta.getByText(/no tiene guardada la respuesta completa/).count()) {
      // HU #14562 · AC39: el primer nivel es una lista de definición (group + dt/dd).
      const respuesta = seccionRespuesta.getByRole("group", { name: "Respuesta de la fuente" });
      await expect(respuesta.getByRole("term").filter({ hasText: /^Cantidad de comparendos$/ })).toBeVisible();
      const tabla = pestana.getByRole("table", { name: "Comparendos" });
      if (await tabla.count()) await expect(tabla.getByRole("columnheader", { name: "Valor" })).toBeVisible();
      else await expect(respuesta.locator("div", { has: pestana.getByRole("term").filter({ hasText: /^Comparendos$/ }) }).getByText("Sin registros")).toBeVisible();
    } else {
      await expect(seccionRespuesta.getByRole("region", { name: /^resultado$/i })).toHaveCount(0);
    }
    // La vista curada sigue sin nombres crudos; la respuesta cruda va aparte, en la tarjeta plegable «Respuesta del proveedor»
    // (D19), cerrada por defecto y expandible con el teclado.
    const curada = seccionRespuesta.getByRole("group", { name: "Respuesta de la fuente" });
    await expect(curada.getByText(/proveedor|desde_cache|http_status|duracion_ms|semaforo/i)).toHaveCount(0);
    const crudo = seccionRespuesta.getByRole("button", { name: "Respuesta del proveedor" });
    if (await crudo.count()) {
      await expect(crudo).toHaveAttribute("aria-expanded", "false");
      await crudo.focus();
      await pestana.keyboard.press("Enter");
      await expect(crudo).toHaveAttribute("aria-expanded", "true");
    }
    // AC5: nada que delate un dato manual.
    await expect(pestana.getByText(/manual/i)).toHaveCount(0);

    // «Volver al diagnóstico» lleva al tablero del lote.
    await pestana.getByRole("link", { name: "Volver al diagnóstico" }).click();
    await expect(pestana).toHaveURL(/\/diagnosticos\/[0-9a-f-]{36}$/i);
    await expect(pestana.getByRole("link", { name: "Volver a diagnósticos" })).toHaveAttribute("href", "/");
    await pestana.close();
  });
});
