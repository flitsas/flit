// HU #14458 — El Bearer de la caja solo viaja en /caja/*; sin sesión el middleware lleva al login; el proxy exige Origin propio.
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("server-only", () => ({}));
const cabecerasEntrantes = new Headers();
vi.mock("next/headers", () => ({
  cookies: async () => ({ get: (n: string) => (n === "pn_sesion" ? { value: "tok-ciudadano" } : n === "pn_punto" ? { value: "tok-punto" } : undefined) }),
  headers: async () => cabecerasEntrantes,
}));
const cajaApiProxy = vi.hoisted(() => vi.fn(async (_r: Request, _p: string[]) => new Response("{}", { status: 200, headers: { "content-type": "application/json" } })));
vi.mock("@/lib/caja/auth.server", () => ({ cajaApiProxy, authRoutes: {} }));
const claimsTokenFromCookieHeader = vi.hoisted(() => vi.fn<(cookie: string | null, producto: string) => Promise<string | null>>());
vi.mock("@flit/auth/request", () => ({ claimsTokenFromCookieHeader }));

import { GET, POST } from "@/app/api/v1/persona-natural/[...path]/route";
import { middleware, config } from "@/middleware";
import { esRutaCaja, MAX_CUERPO_CAJA_BYTES, reenviarAlBff, validarPeticionCaja } from "@/lib/server/proxy";
import { NextRequest } from "next/server";

const contexto = (path: string[]) => ({ params: Promise.resolve({ path }) });

describe("proxy: rama de la caja", () => {
  beforeEach(() => cajaApiProxy.mockClear());

  it("las rutas /caja/* van a @flit/auth (Bearer del producto caja) sin las cookies del ciudadano", async () => {
    const request = new Request("https://portal/api/v1/persona-natural/caja/sesion", { method: "GET" });
    const respuesta = await GET(request, contexto(["caja", "sesion"]));

    expect(respuesta.status).toBe(200);
    expect(cajaApiProxy).toHaveBeenCalledTimes(1);
    expect(cajaApiProxy.mock.calls[0][1]).toEqual(["sesion"]);
    expect(respuesta.headers.get("cache-control")).toBe("no-store");
  });

  it("rechaza un POST de otro origen sin llamar al BFF (CSRF)", async () => {
    const request = new Request("https://portal/api/v1/persona-natural/caja/tramites/PN-2026-000123/confirmar", {
      method: "POST",
      body: "{}",
      headers: { "content-type": "application/json", origin: "https://malo.example" },
    });
    const respuesta = await POST(request, contexto(["caja", "tramites", "PN-2026-000123", "confirmar"]));

    expect(respuesta.status).toBe(403);
    expect(cajaApiProxy).not.toHaveBeenCalled();
  });

  it("deja pasar el POST del propio portal", async () => {
    const request = new Request("https://portal/api/v1/persona-natural/caja/tramites/PN-2026-000123/confirmar", {
      method: "POST",
      body: new FormData(),
      headers: { origin: "https://portal" },
    });
    expect((await POST(request, contexto(["caja", "tramites", "PN-2026-000123", "confirmar"]))).status).toBe(200);
    expect(cajaApiProxy).toHaveBeenCalledTimes(1);
  });

  it("el resto del BFF no recibe el Bearer de la caja", async () => {
    const fetchImpl = vi.fn(async () => new Response("{}", { status: 200 }));
    await reenviarAlBff(new Request("https://portal/api/v1/persona-natural/organismos"), ["organismos"], { apiOrigin: "http://gateway", fetchImpl });
    const cabeceras = (fetchImpl.mock.calls[0] as unknown as [URL, RequestInit])[1].headers as Headers;
    expect(cabeceras.get("authorization")).toBeNull();
  });

  it("reenviarAlBff jamás presta pn_sesion ni pn_punto a una ruta de la caja", async () => {
    const fetchImpl = vi.fn(async () => new Response("{}", { status: 200 }));
    const resultado = await reenviarAlBff(new Request("https://portal/api/v1/persona-natural/caja/sesion"), ["caja", "sesion"], {
      apiOrigin: "http://gateway",
      tokenSesion: "tok-ciudadano",
      tokenPunto: "tok-punto",
      fetchImpl,
    });
    expect(resultado.status).toBe(404);
    expect(fetchImpl).not.toHaveBeenCalled();
  });

  it("detecta la rama y valida ruta, tipo y tamaño del cuerpo", () => {
    expect(esRutaCaja(["caja", "sesion"])).toBe(true);
    expect(esRutaCaja(["punto", "caja"])).toBe(false);

    const base = { method: "POST", headers: { origin: "https://portal" } };
    expect(validarPeticionCaja(new Request("https://portal/x", base), ["caja", "..", "x"])?.status).toBe(400);
    expect(validarPeticionCaja(new Request("https://portal/x", { ...base, headers: { ...base.headers, "content-type": "text/plain" }, body: "a" }), ["caja", "x"])?.status).toBe(415);
    expect(
      validarPeticionCaja(new Request("https://portal/x", { ...base, headers: { ...base.headers, "content-type": "application/json", "content-length": String(MAX_CUERPO_CAJA_BYTES + 1) }, body: "{}" }), ["caja", "x"])?.status,
    ).toBe(413);
    expect(validarPeticionCaja(new Request("https://portal/x", { method: "GET" }), ["caja", "sesion"])).toBeNull();
  });
});

describe("middleware de la caja", () => {
  const pedir = (ruta: string) => new NextRequest(`https://portal${ruta}`, { headers: { cookie: "flit_session_caja=x" } });
  const tokenCon = (claims: object) => `e30.${btoa(JSON.stringify(claims)).replace(/=+$/, "")}.`;

  beforeEach(() => claimsTokenFromCookieHeader.mockReset());

  it("sin sesión de caja redirige al login con returnTo", async () => {
    claimsTokenFromCookieHeader.mockResolvedValue(null);
    const respuesta = await middleware(pedir("/o/5631000/caja"));

    expect(respuesta.status).toBe(307);
    const destino = new URL(respuesta.headers.get("location") ?? "");
    expect(destino.pathname).toBe("/auth/login");
    expect(destino.searchParams.get("returnTo")).toBe("/o/5631000/caja");
    expect(claimsTokenFromCookieHeader).toHaveBeenCalledWith("flit_session_caja=x", "caja");
  });

  it("con sesión vigente deja pasar", async () => {
    claimsTokenFromCookieHeader.mockResolvedValue(tokenCon({ auth_time: Math.floor(Date.now() / 1000) - 60 }));
    const respuesta = await middleware(pedir("/o/5631000/caja"));
    expect(respuesta.headers.get("location")).toBeNull();
  });

  it("con más de 12 h desde la autenticación vuelve al login", async () => {
    claimsTokenFromCookieHeader.mockResolvedValue(tokenCon({ auth_time: Math.floor(Date.now() / 1000) - 13 * 3600 }));
    const respuesta = await middleware(pedir("/o/5631000/caja"));
    expect(new URL(respuesta.headers.get("location") ?? "").pathname).toBe("/auth/login");
  });

  it("sin auth_time (fail-closed) vuelve al login y borra las cookies de la caja", async () => {
    claimsTokenFromCookieHeader.mockResolvedValue(tokenCon({ sub: "u" }));
    const respuesta = await middleware(pedir("/o/5631000/caja"));
    expect(new URL(respuesta.headers.get("location") ?? "").pathname).toBe("/auth/login");
    const borradas = respuesta.headers.getSetCookie().filter((c) => /Max-Age=0/i.test(c));
    expect(borradas.some((c) => c.startsWith("flit_session_caja="))).toBe(true);
    expect(borradas.some((c) => c.startsWith("flit_session_caja.1="))).toBe(true);
  });

  it("el matcher cubre solo /o/:codigo/caja", () => {
    expect(config.matcher).toEqual(["/o/:codigo/caja", "/o/:codigo/caja/:path*"]);
  });
});
