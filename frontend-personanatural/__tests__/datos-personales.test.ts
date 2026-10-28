import { readdirSync, readFileSync, statSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";

// Guardas de Habeas Data (Ley 1581) y de la sesión del trámite: se leen los fuentes de la app.
const RAIZ = path.resolve(__dirname, "..");

function fuentes(dir: string): string[] {
  return readdirSync(dir).flatMap((nombre) => {
    const ruta = path.join(dir, nombre);
    if (nombre === "node_modules" || nombre === ".next" || nombre === "__tests__") return [];
    if (statSync(ruta).isDirectory()) return fuentes(ruta);
    return /\.(ts|tsx)$/.test(nombre) ? [ruta] : [];
  });
}

const codigo = ["app", "lib", "components"].flatMap((d) => fuentes(path.join(RAIZ, d))).map((f) => ({ f: path.relative(RAIZ, f), s: readFileSync(f, "utf8") }));

describe("datos personales fuera del navegador y de la telemetría", () => {
  it("ninguna pantalla ni librería usa localStorage, sessionStorage ni IndexedDB", () => {
    const usos = codigo.filter(({ s }) => /\b(localStorage|sessionStorage|indexedDB)\b/.test(s)).map(({ f }) => f);

    expect(usos).toEqual([]);
  });

  it("nada escribe en consola ni reporta errores con sus datos", () => {
    const usos = codigo.filter(({ s }) => /console\.(log|error|warn|info|debug)\(/.test(s)).map(({ f }) => f);

    expect(usos).toEqual([]);
  });

  it("el token de la sesión solo existe en el servidor: ningún componente de navegador lo lee", () => {
    const cliente = codigo.filter(({ s }) => /^"use client"/.test(s) || /^\s*import .* from "react"/m.test(s));

    expect(cliente.filter(({ s }) => /pn_sesion|COOKIE_SESION|document\.cookie/.test(s)).map(({ f }) => f)).toEqual([]);
  });

  it("el cliente no conoce ninguna URL de FLIT 2: solo el prefijo relativo del portal", () => {
    const urls = codigo.filter(({ f }) => f.startsWith(path.join("lib", "api"))).filter(({ s }) => /https?:\/\//.test(s)).map(({ f }) => f);

    expect(urls).toEqual([]);
  });

  it("no se porta ningún simulador del repo de referencia", () => {
    const simuladores = codigo.filter(({ s }) => /radicado-demo|quote-payment-demo|b2c-demo|ORGANISMOS_INTEGRADOS|TARIFAS_ESTIMADAS/.test(s)).map(({ f }) => f);

    expect(simuladores).toEqual([]);
  });
});
