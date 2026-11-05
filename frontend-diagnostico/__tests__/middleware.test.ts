// @vitest-environment node
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";
import { APP, GATEWAY, HUB, ahora, cabeceraCookie, entornoDeServidor, jwt } from "./sesion-de-prueba";

vi.mock("@flit/auth/request", () => ({ claimsTokenFromCookieHeader: vi.fn() }));

import { claimsTokenFromCookieHeader } from "@flit/auth/request";
import { config, middleware } from "@/middleware";
import { authRoutes } from "@/lib/auth.server";

// Uso de ejemplo: middleware(new NextRequest("https://dev.diagnostico.flitsas.online/")) → 307 a /auth/login?returnTo=/

// El matcher de Next.js es una expresión de ruta; se evalúa como regex anclada para comprobar qué exige sesión.
const exigeSesion = (ruta: string) => new RegExp(`^${config.matcher[0]}$`).test(ruta);

beforeEach(entornoDeServidor);
afterEach(() => {
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
  vi.mocked(claimsTokenFromCookieHeader).mockReset();
});

describe("middleware de Diagnóstico", () => {
  it("sin cookie redirige al login del hub con aud=diagnostico y returnTo", async () => {
    vi.mocked(claimsTokenFromCookieHeader).mockResolvedValue(null);

    const respuesta = await middleware(new NextRequest(`${APP}/vehiculos?placa=ABC123`));

    expect(claimsTokenFromCookieHeader).toHaveBeenCalledWith(null, "diagnostico");
    expect(respuesta.status).toBe(307);
    const destino = new URL(respuesta.headers.get("location")!);
    expect(destino.pathname).toBe("/auth/login");
    expect(destino.searchParams.get("returnTo")).toBe("/vehiculos?placa=ABC123");
  });

  it("con la cookie de claims de diagnostico deja pasar sin redirigir", async () => {
    vi.mocked(claimsTokenFromCookieHeader).mockResolvedValue("cabecera.claims.");

    const respuesta = await middleware(new NextRequest(`${APP}/`, { headers: { cookie: "flit_session_diagnostico=x" } }));

    expect(claimsTokenFromCookieHeader).toHaveBeenCalledWith("flit_session_diagnostico=x", "diagnostico");
    expect(respuesta.headers.get("location")).toBeNull();
    expect(respuesta.headers.get("x-middleware-next")).toBe("1");
  });

  it("exige sesión en las pantallas y deja fuera login, API, salud, 403 y estáticos (sin bucles)", () => {
    expect(exigeSesion("/")).toBe(true);
    expect(exigeSesion("/vehiculos")).toBe(true);
    for (const libre of ["/auth/login", "/auth/callback", "/api/v1/diagnostico/sesion", "/healthz", "/403", "/_next/static/x.js", "/assets/favicon.svg", "/icon.svg"]) {
      expect(exigeSesion(libre), libre).toBe(false);
    }
  });

  it("el login lleva al authorize del hub con PKCE S256 y client_id (aud) diagnostico", async () => {
    const respuesta = await authRoutes.login(new Request(`${APP}/auth/login?returnTo=%2Fvehiculos`));

    expect(respuesta.status).toBe(302);
    const destino = new URL(respuesta.headers.get("location")!);
    expect(destino.origin + destino.pathname).toBe(`${HUB}/connect/authorize`);
    expect(destino.searchParams.get("client_id")).toBe("diagnostico");
    expect(destino.searchParams.get("response_type")).toBe("code");
    expect(destino.searchParams.get("code_challenge_method")).toBe("S256");
    expect(destino.searchParams.get("code_challenge")).toBeTruthy();
    expect(destino.searchParams.get("redirect_uri")).toBe(`${APP}/auth/callback`);
  });
});

describe("regreso del hub", () => {
  it("tras autenticarse en el hub vuelve a Diagnóstico, a la ruta pedida, con la sesión iniciada", async () => {
    const login = await authRoutes.login(new Request(`${APP}/auth/login?returnTo=%2Fvehiculos`));
    const state = new URL(login.headers.get("location")!).searchParams.get("state")!;
    const accessToken = jwt({ sub: "u-1", aud: "diagnostico", tenant_id: "t-1", exp: ahora() + 900 });
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ access_token: accessToken, refresh_token: "r-1", expires_in: 900 }));
    vi.stubGlobal("fetch", fetchMock);

    const respuesta = await authRoutes.callback(
      new Request(`${APP}/auth/callback?code=codigo-1&state=${state}`, { headers: { cookie: cabeceraCookie(login.headers.getSetCookie()) } }),
    );

    expect(respuesta.status).toBe(302);
    expect(respuesta.headers.get("location")).toBe("/vehiculos");
    expect(fetchMock.mock.calls[0][0]).toBe(`${GATEWAY}/connect/token`);
    const sesion = respuesta.headers.getSetCookie().find((c) => c.startsWith("flit_session_diagnostico"));
    expect(sesion).toBeDefined();
    expect(sesion).toContain("HttpOnly");
  });
});
