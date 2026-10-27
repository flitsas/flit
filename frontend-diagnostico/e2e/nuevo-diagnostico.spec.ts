import { expect, test, type Page } from "@playwright/test";

/**
 * HU #14085 — pantalla única de creación (Express | Masivo).
 *
 * Artefacto E2E para QA: necesita la suite levantada (hub en 4022, gateway en 4002, core-diagnostico en 4029 con
 * `Diagnostico__Simulacion__Habilitada=true`, y esta app en 4024). Excluido de `tsc` y de Vitest; se corre con Playwright
 * desde la raíz del repo, por ejemplo:
 *
 *   DIAGNOSTICO_URL=http://127.0.0.1:4024 E2E_USUARIO=... E2E_CLAVE=... \
 *   pnpm --dir frontend exec playwright test ../frontend-diagnostico/e2e/nuevo-diagnostico.spec.ts
 *
 * E2E_USUARIO es un usuario de la compañía con diagnosticos.crear y diagnosticos.ver. Las credenciales son de usuarios de
 * prueba del ambiente DEV y entran solo por variables de entorno. Las placas son ficticias (TST…).
 */
const APP = process.env.DIAGNOSTICO_URL ?? "http://127.0.0.1:4024";
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

  test("AC1/AC6: desde el inicio, Diagnosticar abre Express con la placa; en 1280 px dos columnas con Parámetros sticky", async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.getByRole("textbox", { name: "Placa o VIN" }).fill(PLACA);
    await page.getByRole("button", { name: "Diagnosticar" }).click();
    await page.waitForURL((url) => url.pathname === "/diagnosticos/nuevo" && url.searchParams.get("modo") === "express");

    const vehiculos = page.getByRole("region", { name: "Vehículos" });
    const parametros = page.getByRole("region", { name: "Parámetros" });
    await expect(parametros.getByRole("textbox", { name: "Nombre del diagnóstico" })).toHaveValue(new RegExp(`^Express ${PLACA} `));
    const [v, p] = [await vehiculos.boundingBox(), await parametros.boundingBox()];
    expect(v && p && p.x > v.x + v.width - 1).toBeTruthy();
    expect(await parametros.evaluate((el) => getComputedStyle(el).position)).toBe("sticky");
    await sinDesbordeHorizontal(page);
  });

  test("AC1: en 375 px las zonas se apilan sin scroll horizontal", async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 800 });
    await page.goto(`${APP}/diagnosticos/nuevo?modo=masivo`);
    const vehiculos = page.getByRole("region", { name: "Vehículos" });
    const parametros = page.getByRole("region", { name: "Parámetros" });
    await expect(parametros.getByRole("button", { name: "Validar archivo" })).toBeVisible();
    const [v, p] = [await vehiculos.boundingBox(), await parametros.boundingBox()];
    expect(v && p && p.y >= v.y + v.height - 1).toBeTruthy();
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
