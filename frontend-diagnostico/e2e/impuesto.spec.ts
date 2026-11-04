import { expect, test, type Page } from "@playwright/test";
import { lanzarExpress } from "./express";

/**
 * HU #13866 (F09-H7) y C3-8 — celda y detalle del impuesto departamental en el tablero.
 *
 * Artefacto E2E para QA, SOLO en LOCAL: necesita la suite levantada (hub en 4022, gateway en 4002, core-diagnostico en 4032
 * con `Diagnostico__Ambiente=LOCAL`, `Diagnostico__Simulacion__Habilitada=true` y `Diagnostico__Fuentes__ImpuestoDisponible=true`,
 * y esta app en 4024). En simulación el alta ofrece la fuente cuando la bandera está encendida y la réplica tiene
 * departamentos. El contrato de la compañía debe habilitar IMPUESTO_DEPARTAMENTAL; si no, el test se omite.
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
  test("un Express con el impuesto llega a verde y su detalle (pestaña nueva) muestra el monto total y las vigencias (HU #14087)", async ({ page, context }) => {
    test.skip(!process.env.E2E_USUARIO || !process.env.E2E_CLAVE, "Faltan E2E_USUARIO / E2E_CLAVE");
    await entrar(page, process.env.E2E_USUARIO!, process.env.E2E_CLAVE!);

    // El alta (pantalla única) ofrece el impuesto cuando la simulación lo declara disponible.
    await page.getByRole("textbox", { name: "Placa o VIN" }).fill(PLACA);
    await page.getByRole("button", { name: "Diagnosticar" }).click();
    await page.waitForURL((url) => url.pathname === "/diagnosticos/nuevo" && url.searchParams.get("modo") === "express");
    const casilla = page.getByRole("checkbox", { name: "Impuesto departamental" });
    test.skip((await casilla.count()) === 0, "La simulación no ofrece el impuesto: revisar la bandera y la réplica de departamentos");
    await expect(casilla).toBeVisible();
    await page.goto(`${APP}/`);
    await lanzarExpress(page, PLACA);

    const grilla = page.getByRole("table", { name: /semáforo de consultas/i });
    await expect(grilla).toBeVisible();
    // HU #14087: las 8 fuentes salen siempre; sin el impuesto pedido su celda dice «Sin solicitar» y no es enlace.
    test.skip(
      (await grilla.getByRole("link", { name: new RegExp(`^Ver detalle de Impuesto departamental de ${PLACA}`) }).count()) === 0,
      "El Express no incluye el impuesto: revisar la bandera ImpuestoDisponible y el contrato de la compañía",
    );

    // AC1: sin recargar, el semáforo del impuesto llega a verde (o a N/A si el RUNT simulado no deriva un departamento soportado).
    const semaforo = grilla.getByRole("link", { name: new RegExp(`^Ver detalle de Impuesto departamental de ${PLACA}`) });
    await expect(semaforo).toHaveAccessibleName(/(Ejecutado \(OK\)|Novedad) \(se abre en una pestaña nueva\)$/, { timeout: 60_000 });
    test.skip(/Novedad \(/.test((await semaforo.getAttribute("aria-label")) ?? ""), "El vehículo simulado quedó en un departamento no soportado");

    await semaforo.focus();
    const [pestana] = await Promise.all([context.waitForEvent("page"), page.keyboard.press("Enter")]);
    await pestana.waitForLoadState();
    await expect(pestana.getByRole("heading", { level: 1, name: `Impuesto departamental · ${PLACA}` })).toBeVisible();
    // HU #14087: la respuesta de la fuente en español (monto total sin formato, vigencias con año/valor/pagada en Sí/No).
    const respuesta = pestana.getByRole("table", { name: "Respuesta de la fuente" });
    await expect(respuesta.getByRole("rowheader", { name: "Monto total" })).toBeVisible();
    await expect(respuesta.getByRole("row", { name: /Monto total\s*\d+$/ })).toBeVisible();
    const tabla = pestana.getByRole("table", { name: "Vigencias" });
    if (await tabla.count()) {
      await expect(tabla.getByRole("columnheader", { name: "Pagada" })).toBeVisible();
      await expect(tabla.getByText(/^(Sí|No)$/).first()).toBeVisible();
    } else {
      await expect(respuesta.getByRole("row", { name: /Vigencias\s*Sin registros/ })).toBeVisible();
    }
    // Sin la auditoría del proveedor y nada delata un dato manual (R-20).
    await expect(pestana.getByRole("region", { name: "Respuesta de la fuente" }).getByText(/proveedor/i)).toHaveCount(0);
    await expect(pestana.getByText(/manual|a mano/i)).toHaveCount(0);
    await pestana.close();
  });
});
