import { expect, type Page } from "@playwright/test";

/**
 * Lanza un Express desde la tarjeta del título del inicio (HU #14085): «Placa o VIN» + «Diagnosticar en línea» abre la pantalla
 * única /diagnosticos/nuevo en Express con la placa precargada; el botón principal valida («Continuar» si la operación muestra
 * el estimado, ANTES_DE_LANZAR, e «Iniciar diagnóstico» si no, HU #14562) y, con estimado, «Lanzar diagnóstico» confirma; en el resultado «Ver tablero» lleva al tablero /diagnosticos/{id}.
 */
export async function lanzarExpress(page: Page, placa: string, nombre?: string) {
  await page.getByRole("textbox", { name: "Placa o VIN" }).fill(placa);
  await page.getByRole("button", { name: "Diagnosticar en línea" }).click();
  await page.waitForURL((url) => url.pathname === "/diagnosticos/nuevo" && url.searchParams.get("modo") === "express");
  await expect(page.getByRole("textbox", { name: "Placa o VIN" })).toHaveValue(placa);
  if (nombre) await page.getByRole("textbox", { name: "Nombre del diagnóstico" }).fill(nombre);
  await page.getByRole("button", { name: /^(Continuar|Iniciar diagnóstico)$/ }).click();

  const tablero = /\/diagnosticos\/[0-9a-f-]{36}$/i;
  const lanzar = page.getByRole("button", { name: "Lanzar diagnóstico" });
  // Con estimado_consultas = ANTES_DE_LANZAR aparece el estimado; si no, el Express se lanza directo.
  await Promise.race([lanzar.waitFor({ state: "visible" }), page.waitForURL(tablero)]);
  if (await lanzar.isVisible()) await lanzar.click();
  // Pantalla de resultado (AC18): la app redirige sola y cualquier foco o clic sobre la pantalla cancela esa redirección
  // («Detuvimos la redirección…»), así que pulsar «Ver tablero» competía con ella. Se navega al href del enlace.
  const verTablero = page.getByRole("link", { name: "Ver tablero" });
  const enTablero = () => tablero.test(new URL(page.url()).pathname);
  await Promise.race([verTablero.waitFor({ state: "visible" }), page.waitForURL(tablero, { waitUntil: "commit" })]);
  if (!enTablero()) {
    const href = await verTablero.getAttribute("href", { timeout: 2_000 }).catch(() => null);
    if (href && !enTablero()) await page.goto(new URL(href, page.url()).href, { waitUntil: "commit" }).catch(() => undefined);
  }
  await page.waitForURL(tablero, { waitUntil: "commit" });
  await page.waitForLoadState("domcontentloaded");
}
