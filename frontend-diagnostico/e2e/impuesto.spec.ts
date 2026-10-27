import { expect, test, type Page } from "@playwright/test";
import { lanzarExpress } from "./express";

/**
 * HU #13866 (F09-H7) — celda y detalle del impuesto departamental en el tablero.
 *
 * Artefacto E2E para QA, SOLO en LOCAL: necesita la suite levantada (hub en 4022, gateway en 4002, core-diagnostico en 4029
 * con `Diagnostico__Ambiente=LOCAL`, `Diagnostico__Simulacion__Habilitada=true` y `Diagnostico__Fuentes__ImpuestoDisponible=true`
 * solo en el entorno de este test, y esta app en 4024). La capacidad simulada declara los departamentos 05 y 25 (HU #13861),
 * así que el impuesto queda disponible sin C3 y sin tocar ningún ambiente desplegado. La consulta simulada (HU #13862) con
 * placa terminada en 1 devuelve 2025 y 2026 sin pagar: total adeudado $ 400.000. El contrato de la compañía debe habilitar
 * IMPUESTO_DEPARTAMENTAL y el Express debe incluirlo en el alcance; si no, el test se omite con el motivo.
 * Excluido de `tsc` y de Vitest; se corre con Playwright desde la raíz del repo, por ejemplo:
 *
 *   DIAGNOSTICO_URL=http://127.0.0.1:4024 E2E_USUARIO=... E2E_CLAVE=... \
 *   pnpm --dir frontend exec playwright test ../frontend-diagnostico/e2e/impuesto.spec.ts
 *
 * Las credenciales son de usuarios de prueba y entran solo por variables de entorno.
 */
const APP = process.env.DIAGNOSTICO_URL ?? "http://127.0.0.1:4024";
/** Placa de prueba terminada en 1: RUNT e impuesto simulados responden con éxito. */
const PLACA = process.env.E2E_PLACA_IMPUESTO ?? "TST201";

async function entrar(page: Page, usuario: string, clave: string) {
  await page.goto(`${APP}/`);
  await page.waitForURL((url) => !url.href.startsWith(APP));
  await page.getByLabel(/correo|usuario/i).fill(usuario);
  await page.getByLabel(/contraseña/i).fill(clave);
  await page.getByRole("button", { name: /ingresar|iniciar sesión/i }).click();
  await page.waitForURL(`${APP}/`);
  await expect(page.getByRole("heading", { level: 1, name: "Diagnóstico vehicular" })).toBeVisible();
}

test.describe("impuesto departamental en el tablero", () => {
  test("un Express con el impuesto llega a verde y su detalle muestra monto_total y las vigencias (HU #14087)", async ({ page }) => {
    test.skip(!process.env.E2E_USUARIO || !process.env.E2E_CLAVE, "Faltan E2E_USUARIO / E2E_CLAVE");
    await entrar(page, process.env.E2E_USUARIO!, process.env.E2E_CLAVE!);

    // HU #14085: el inicio abre la pantalla única en Express con la placa precargada.
    await lanzarExpress(page, PLACA);

    const grilla = page.getByRole("table", { name: /semáforo de consultas/i });
    await expect(grilla).toBeVisible();
    // HU #14087: las 8 fuentes salen siempre; sin el impuesto pedido su celda dice «Sin solicitar» y no es botón.
    test.skip(
      (await grilla.getByRole("button", { name: new RegExp(`^Ver detalle de Impuesto departamental de ${PLACA}`) }).count()) === 0,
      "El Express no incluye el impuesto: revisar la bandera ImpuestoDisponible y el contrato de la compañía",
    );

    // AC1: sin recargar, el semáforo del impuesto llega a verde (o a N/A si el RUNT simulado no deriva un departamento soportado).
    const semaforo = grilla.getByRole("button", { name: new RegExp(`^Ver detalle de Impuesto departamental de ${PLACA}`) });
    await expect(semaforo).toHaveAccessibleName(/(Ejecutado \(OK\)|Novedad)$/, { timeout: 60_000 });
    test.skip(/Novedad$/.test((await semaforo.getAttribute("aria-label")) ?? ""), "El vehículo simulado quedó en un departamento no soportado");

    await semaforo.focus();
    await page.keyboard.press("Enter");
    const dialogo = page.getByRole("dialog", { name: `Impuesto departamental · ${PLACA}` });
    await expect(dialogo).toBeVisible();
    // HU #14087: la respuesta de la fuente pinta los campos verbatim (monto_total sin formato, vigencias con anio/valor/pagada).
    const respuesta = dialogo.getByRole("table", { name: "Respuesta de la fuente" });
    await expect(respuesta.getByRole("rowheader", { name: "monto_total" })).toBeVisible();
    await expect(respuesta.getByRole("row", { name: /monto_total\s*\d+$/ })).toBeVisible();
    const tabla = dialogo.getByRole("table", { name: "vigencias" });
    if (await tabla.count()) {
      await expect(tabla.getByRole("columnheader", { name: "pagada" })).toBeVisible();
      await expect(tabla.getByText(/^(true|false)$/).first()).toBeVisible();
    } else {
      await expect(respuesta.getByRole("row", { name: /vigencias\s*\[\]/ })).toBeVisible();
    }
    // HU #14087 AC8 ajustado: con la respuesta completa guardada se ve todo lo que respondió la fuente (también resultado.proveedor);
    // sin ella, el aviso de resultado resumido. Nada delata un dato manual (R-20).
    await expect(dialogo.getByText(/manual|a mano/i)).toHaveCount(0);

    await page.keyboard.press("Escape");
    await expect(dialogo).toHaveCount(0);
    await expect(semaforo).toBeFocused();
  });
});
