import { afterEach, describe, expect, it, vi } from "vitest";
import { parseCookies, txCookie } from "../cookies";
import { base64UrlEncode, unseal } from "../crypto";
import { createAuthRoutes, safeReturnTo } from "../routes";
import type { AuthConfig } from "../config";

// Bug (registro diferido, 2026-10-21) — open redirect en safeReturnTo: un tabulador, CR, LF u otro caracter de
// control tras la barra inicial («/\t/evil.com») lo quita el navegador y la ruta queda «//evil.com», otro sitio.
// Uso de ejemplo: safeReturnTo("/diagnosticos/123?x=1") → "/diagnosticos/123?x=1"; safeReturnTo("/\t/evil.com") → "/".

const config: AuthConfig = {
  productCode: "tramites",
  hubUrl: "https://dev.flitsas.online",
  oidcInternalUrl: "http://gateway:4002",
  apiOrigin: "http://gateway:4002",
  sessionSecret: "s".repeat(40),
  internalApiKey: "clave",
  appUrl: undefined,
};
const routes = createAuthRoutes({ productCode: "tramites", config: () => config });
const APP = "https://dev.tramites.flitsas.online";

afterEach(() => vi.unstubAllGlobals());

const CONTROL = [...Array.from({ length: 0x20 }, (_, i) => i), 0x7f].map((c) => String.fromCharCode(c));
const hex = (c: string) => `%${c.charCodeAt(0).toString(16).padStart(2, "0")}`;

describe("safeReturnTo — rechaza lo que saca al usuario de la app", () => {
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
    expect(safeReturnTo(value)).toBe("/");
  });

  it("cualquier caracter de control (\\x00-\\x1f, \\x7f) en cualquier posición → /", () => {
    for (const c of CONTROL) {
      expect(safeReturnTo(`/${c}/evil.com`), `código ${hex(c)}`).toBe("/");
      expect(safeReturnTo(`/ruta${c}`), `código ${hex(c)} al final`).toBe("/");
    }
  });

  it("null → /", () => {
    expect(safeReturnTo(null)).toBe("/");
  });
});

describe("safeReturnTo — acepta las rutas internas de hoy", () => {
  it.each([
    "/",
    "/configuracion",
    "/diagnosticos/123?x=1",
    "/tramites?x=1",
    "/?inicio=1",
    "/vehiculos?placa=ABC123",
    "/auth/listo?to=%2Ftramites%3Festado%3Dabierto",
    "/ruta#seccion",
  ])("%s se conserva igual", (value) => {
    expect(safeReturnTo(value)).toBe(value);
  });
});

describe("/auth/login — returnTo decodificado del query", () => {
  async function returnToGuardado(query: string): Promise<string> {
    const response = await routes.login(new Request(`${APP}/auth/login?returnTo=${query}`));
    const raw = parseCookies(response.headers.getSetCookie().map((c) => c.split(";")[0]).join("; ")).get(txCookie("tramites"))!;
    const tx = await unseal<{ returnTo: string }>(raw, config.sessionSecret);
    return tx!.returnTo;
  }

  it.each(["%2F%09%2Fevil.com", "/%09/evil.com", "/%0a/evil.com", "/%0D/evil.com", "/%00/evil.com", "/%7f/evil.com", "%2F%2Fevil.com", "%2F%5Cevil.com", "https%3A%2F%2Fevil.com"])(
    "%s → la transacción guarda /",
    async (query) => {
      expect(await returnToGuardado(query)).toBe("/");
    },
  );

  it("todo caracter de control codificado (%00-%1f, %7f) → /", async () => {
    for (const c of CONTROL) expect(await returnToGuardado(`/${hex(c)}/evil.com`), hex(c)).toBe("/");
  });

  it("una ruta interna codificada se conserva", async () => {
    expect(await returnToGuardado("%2Fdiagnosticos%2F123%3Fx%3D1")).toBe("/diagnosticos/123?x=1");
  });

  it("el callback con returnTo=/%09/evil.com no emite Location fuera de la app", async () => {
    const login = await routes.login(new Request(`${APP}/auth/login?returnTo=/%09/evil.com`));
    const state = new URL(login.headers.get("location")!).searchParams.get("state")!;
    const enc = (o: unknown) => base64UrlEncode(new TextEncoder().encode(JSON.stringify(o)));
    const accessToken = `${enc({ alg: "RS256" })}.${enc({ sub: "u1", aud: "tramites", exp: Math.floor(Date.now() / 1000) + 900 })}.firma`;
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(Response.json({ access_token: accessToken, refresh_token: "r1", expires_in: 900 })));

    const response = await routes.callback(new Request(`${APP}/auth/callback?code=c1&state=${state}`, {
      headers: { cookie: login.headers.getSetCookie().map((c) => c.split(";")[0]).join("; ") },
    }));

    expect(response.status).toBe(302);
    expect(response.headers.get("location")).toBe("/");
  });
});
