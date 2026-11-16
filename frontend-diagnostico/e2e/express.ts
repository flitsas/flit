import { expect, type Page } from "@playwright/test";

/**
 * Lanza un Express desde la tarjeta del título del inicio (HU #14085): «Placa o VIN» + «Diagnosticar» abre la pantalla
 * única /diagnosticos/nuevo en Express con la placa precargada; «Continuar» valida y, si la operación muestra el estimado
 * (ANTES_DE_LANZAR), «Lanzar diagnóstico» confirma. Termina en el tablero /diagnosticos/{id}.
 */
export async function lanzarExpress(page: Page, placa: string, nombre?: string) {
  await page.getByRole("textbox", { name: "Placa o VIN" }).fill(placa);
  await page.getByRole("button", { name: "Diagnosticar" }).click();
  await page.waitForURL((url) => url.pathname === "/diagnosticos/nuevo" && url.searchParams.get("modo") === "express");
  await expect(page.getByRole("textbox", { name: "Placa o VIN" })).toHaveValue(placa);
  if (nombre) await page.getByRole("textbox", { name: "Nombre del diagnóstico" }).fill(nombre);
  await page.getByRole("button", { name: "Continuar" }).click();

  const tablero = /\/diagnosticos\/[0-9a-f-]{36}$/i;
  const lanzar = page.getByRole("button", { name: "Lanzar diagnóstico" });
  // Con estimado_consultas = ANTES_DE_LANZAR aparece el estimado; si no, el Express se lanza directo.
  await Promise.race([lanzar.waitFor({ state: "visible" }), page.waitForURL(tablero)]);
  if (await lanzar.isVisible()) await lanzar.click();
  await page.waitForURL(tablero);
}
