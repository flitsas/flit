import { expect, test, type Page } from "@playwright/test";

/**
 * HU #14085 — pantalla única de creación (Express | Masivo).
 *
 * Artefacto E2E para QA: necesita la suite levantada (hub en 4022, gateway en 4002, core-diagnostico en 4032 con
 * `Diagnostico__Simulacion__Habilitada=true`, y esta app en 4024). Excluido de `tsc` y de Vitest; se corre con Playwright
 * desde la raíz del repo, por ejemplo:
 *
 *   DIAGNOSTICO_URL=http://127.0.0.1:4024 E2E_USUARIO=... E2E_CLAVE=... \
 *   pnpm --dir frontend exec playwright test ../frontend-diagnostico/e2e/nuevo-diagnostico.spec.ts
 *
 * E2E_USUARIO es un usuario de la compañía con diagnosticos.crear y diagnosticos.ver. Las credenciales son de usuarios de
 * prueba del ambiente DEV y entran solo por variables de entorno. Las placas son ficticias (TST…).
 *
 * Lista congelada (HU #14562 · AC55): nombres accesibles y atributos data-* de los que depende este spec.
 * Si uno cambia por decisión de UX, el spec se ajusta en el mismo PR de la pantalla.
 *   - button «/ingresar|iniciar sesión/i»
 *   - heading «Diagnóstico vehicular»
 *   - textbox «Placa o VIN»
 *   - button «Diagnosticar en línea»
 *   - region «Vehículos»
 *   - region «Parámetros»
 *   - textbox «Nombre del diagnóstico»
 *   - button «Validar archivo»
 *   - radio «Express»
 *   - radio «Masivo»
 *   - region «Resumen de validación»
 *   Ola 2026-11-15 (FE-3): la carga es de una columna (Parámetros arriba, sin sticky) y la CTA «Validar archivo» sale de
 *   la región «Parámetros» a la barra de acción del final. El detalle del layout lo cubre carga-una-columna.spec.ts.
 *   - radiogroup «Tipo de diagnóstico»
 *   - etiqueta «/correo|usuario/i»
 *   - etiqueta «Contraseña» (exacta: el hub añadió el botón «Mostrar contraseña»)
 *   - etiqueta «/archivo de vehículos/i»
 */
const APP = process.env.DIAGNOSTICO_URL ?? "http://127.0.0.1:4024";
const PLACA = process.env.E2E_PLACA_EXPRESS ?? "TST101";

async function entrar(page: Page, usuario: string, clave: string) {
  // Ola 2026-11-15 (FE-4): «/» es el Dashboard; el listado y la tarjeta «Placa o VIN» viven en /diagnosticos.
  await page.goto(`${APP}/diagnosticos`);
  await page.waitForURL((url) => !url.href.startsWith(APP));
  await page.getByLabel(/correo|usuario/i).fill(usuario);
  await page.getByLabel("Contraseña", { exact: true }).fill(clave);
  await page.getByRole("button", { name: /ingresar|iniciar sesión/i }).click();
  await page.waitForURL(`${APP}/diagnosticos`);
  await expect(page.getByRole("heading", { level: 1, name: "Diagnóstico vehicular" })).toBeVisible();
}

/** Sin scroll horizontal en la página. */
async function sinDesbordeHorizontal(page: Page) {
  const desborde = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(desborde).toBeLessThanOrEqual(0);
}

test.describe("pantalla única de creación", () => {
  test.beforeEach(async ({ page }) => {
    test.skip(!process.env.E2E_USUARIO || !process.env.E2E_CLAVE, "Faltan E2E_USUARIO / E2E_CLAVE");
    await entrar(page, process.env.E2E_USUARIO!, process.env.E2E_CLAVE!);
  });

  test("AC1/AC6: desde el inicio, Diagnosticar en línea abre Express con la placa; en 1280 px Parámetros arriba de Vehículos, sin sticky (FE-3)", async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.getByRole("textbox", { name: "Placa o VIN" }).fill(PLACA);
    await page.getByRole("button", { name: "Diagnosticar en línea" }).click();
    await page.waitForURL((url) => url.pathname === "/diagnosticos/nuevo" && url.searchParams.get("modo") === "express");

    const vehiculos = page.getByRole("region", { name: "Vehículos" });
    const parametros = page.getByRole("region", { name: "Parámetros" });
    await expect(parametros.getByRole("textbox", { name: "Nombre del diagnóstico" })).toHaveValue(new RegExp(`^Express ${PLACA}$`));
    const [v, p] = [await vehiculos.boundingBox(), await parametros.boundingBox()];
    // Ola 2026-11-15 (FE-3): una columna; antes Parámetros iba a la derecha y sticky.
    expect(v && p && p.y + p.height <= v.y + 1).toBeTruthy();
    expect(await parametros.evaluate((el) => getComputedStyle(el).position)).not.toBe("sticky");
    await sinDesbordeHorizontal(page);
  });

  test("AC1: en 375 px las zonas se apilan sin scroll horizontal", async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 800 });
    await page.goto(`${APP}/diagnosticos/nuevo?modo=masivo`);
    const vehiculos = page.getByRole("region", { name: "Vehículos" });
    const parametros = page.getByRole("region", { name: "Parámetros" });
    // Ola 2026-11-15 (FE-3): la CTA va en la barra de acción del final (fuera de Parámetros) y Parámetros va primero.
    const validar = page.getByRole("button", { name: "Validar archivo" });
    await expect(validar).toBeVisible();
    const [v, p, b] = [await vehiculos.boundingBox(), await parametros.boundingBox(), await validar.boundingBox()];
    expect(v && p && v.y >= p.y + p.height - 1).toBeTruthy();
    expect(v && b && b.y >= v.y + v.height - 1).toBeTruthy();
    await sinDesbordeHorizontal(page);
  });

  test("AC8: el selector se opera con flechas y refleja el modo en la URL", async ({ page }) => {
    await page.goto(`${APP}/diagnosticos/nuevo`);
    const express = page.getByRole("radio", { name: "Express" });
    await expect(express).toHaveAttribute("aria-checked", "true");
    await express.focus();
    await page.keyboard.press("ArrowRight");
    await expect(page.getByRole("radio", { name: "Masivo" })).toHaveAttribute("aria-checked", "true");
    await expect(page.getByRole("radio", { name: "Masivo" })).toBeFocused();
    await expect(page).toHaveURL(/modo=masivo/);
  });

  test("AC3/AC5: Masivo en un paso — crea la cabecera, analiza el archivo y bloquea el selector", async ({ page }) => {
    await page.goto(`${APP}/diagnosticos/nuevo?modo=masivo`);
    await page.getByRole("textbox", { name: "Nombre del diagnóstico" }).fill(`E2E masivo ${Date.now()}`);
    await page.getByLabel(/archivo de vehículos/i).setInputFiles({
      name: "lote-e2e.csv",
      mimeType: "text/csv",
      buffer: Buffer.from(`Identificador_Vehiculo\n${PLACA}\n`),
    });
    const [crear, analizar] = await Promise.all([
      page.waitForResponse((r) => r.request().method() === "POST" && new URL(r.url()).pathname.endsWith("/diagnosticos")),
      page.waitForResponse((r) => r.request().method() === "POST" && new URL(r.url()).pathname.endsWith("/carga")),
      page.getByRole("button", { name: "Validar archivo" }).click(),
    ]);
    expect(crear.status()).toBe(201);
    expect(analizar.status()).toBe(200);
    await expect(page.getByRole("region", { name: "Resumen de validación" })).toBeVisible();
    await expect(page.getByRole("radiogroup", { name: "Tipo de diagnóstico" })).toHaveAttribute("aria-disabled", "true");
    await expect(page).toHaveURL(/\/diagnosticos\/nuevo/);
  });
});
