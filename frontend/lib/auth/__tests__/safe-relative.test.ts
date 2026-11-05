import { describe, expect, it } from "vitest";
import { safeRelative } from "../safe-relative";

// Bug (registro diferido, 2026-10-21) — open redirect, segundo punto: /auth/listo?to=/%09/evil.com. El navegador quita
// el tabulador (y CR, LF) al resolver la URL, «/\t/evil.com» queda «//evil.com» y window.location.replace sale de la app.
// Mismos casos que packages/auth/src/__tests__/safe-return-to.test.ts (safeReturnTo de @flit/auth).
// Uso de ejemplo: safeRelative("/tramites?x=1") → "/tramites?x=1"; safeRelative("/\t/evil.com") → "/".

const CONTROL = [...Array.from({ length: 0x20 }, (_, i) => i), 0x7f].map((c) => String.fromCharCode(c));
const hex = (c: string) => `%${c.charCodeAt(0).toString(16).padStart(2, "0")}`;
/** Lo que lee /auth/listo: el parámetro `to` decodificado por URLSearchParams. */
const fromQuery = (query: string) => safeRelative(new URLSearchParams(`to=${query}`).get("to"));

describe("safeRelative — rechaza lo que saca al usuario de la app", () => {
  it.each([
    ["tabulador", "/\t/evil.com"],
    ["salto de línea", "/\n/evil.com"],
    ["retorno de carro", "/\r/evil.com"],
    ["doble barra", "//evil.com"],
    ["barra invertida", "/\\evil.com"],
    ["barra invertida más adentro", "/\t\\evil.com"],
    ["URL absoluta", "https://evil.com"],
    ["esquema sin barras", "javascript:alert(1)"],
    ["vacío", ""],
  ])("%s → /", (_nombre, value) => {
    expect(safeRelative(value)).toBe("/");
  });

  it("cualquier caracter de control (\\x00-\\x1f, \\x7f) en cualquier posición → /", () => {
    for (const c of CONTROL) {
      expect(safeRelative(`/${c}/evil.com`), `código ${hex(c)}`).toBe("/");
      expect(safeRelative(`/ruta${c}`), `código ${hex(c)} al final`).toBe("/");
    }
  });

  it("null y undefined → /", () => {
    expect(safeRelative(null)).toBe("/");
    expect(safeRelative(undefined)).toBe("/");
  });
});

describe("/auth/listo — `to` decodificado del query", () => {
  it.each(["%2F%09%2Fevil.com", "/%09/evil.com", "/%0a/evil.com", "/%0D/evil.com", "/%00/evil.com", "/%7f/evil.com", "%2F%2Fevil.com", "%2F%5Cevil.com", "https%3A%2F%2Fevil.com"])(
    "to=%s → /",
    (query) => {
      expect(fromQuery(query)).toBe("/");
    },
  );

  it("todo caracter de control codificado (%00-%1f, %7f) → /", () => {
    for (const c of CONTROL) expect(fromQuery(`/${hex(c)}/evil.com`), hex(c)).toBe("/");
  });

  it("una ruta interna codificada se conserva", () => {
    expect(fromQuery("%2Ftramites%3Festado%3Dabierto")).toBe("/tramites?estado=abierto");
  });
});

describe("safeRelative — acepta las rutas internas de hoy", () => {
  it.each(["/", "/configuracion", "/diagnosticos/123?x=1", "/tramites?x=1", "/?inicio=1", "/vehiculos?placa=ABC123", "/auth/listo?to=%2Ftramites%3Festado%3Dabierto", "/ruta#seccion"])(
    "%s se conserva igual",
    (value) => {
      expect(safeRelative(value)).toBe(value);
    },
  );
});
