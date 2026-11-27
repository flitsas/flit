// @vitest-environment node
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";
import { APP, GATEWAY, HUB, ahora, cabeceraCookie, entornoDeServidor, jwt } from "./sesion-de-prueba";

vi.mock("@flit/auth/request", () => ({ claimsTokenFromCookieHeader: vi.fn() }));

import { claimsTokenFromCookieHeader } from "@flit/auth/request";
import { config, middleware } from "@/middleware";
import { authRoutes } from "@/lib/auth.server";

// Uso de ejemplo: middleware(new NextRequest("https://dev.impuestos.flitsas.online/")) → 307 a /auth/login?returnTo=/
// F6-CF1 / AC-FE-07: login propio del producto (OIDC con PKCE, client_id = aud = impuestos, cookie flit_session_impuestos).

// El matcher de Next.js es una expresión de ruta; se evalúa como regex anclada para comprobar qué exige sesión.
const exigeSesion = (ruta: string) => new RegExp(`^${config.matcher[0]}$`).test(ruta);

beforeEach(entornoDeServidor);
afterEach(() => {
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
  vi.mocked(claimsTokenFromCookieHeader).mockReset();
});

describe("middleware de Impuestos", () => {
  it("sin cookie redirige al login con aud=impuestos y conserva la ruta y la query en returnTo", async () => {
    vi.mocked(claimsTokenFromCookieHeader).mockResolvedValue(null);

    const respuesta = await middleware(new NextRequest(`${APP}/liquidaciones?empresa=e-1&estado=TERMINADA`));

    expect(claimsTokenFromCookieHeader).toHaveBeenCalledWith(null, "impuestos");
    expect(respuesta.status).toBe(307);
    const destino = new URL(respuesta.headers.get("location")!);
    expect(destino.pathname).toBe("/auth/login");
    expect(destino.searchParams.get("returnTo")).toBe("/liquidaciones?empresa=e-1&estado=TERMINADA");
  });

  it("con la cookie flit_session_impuestos deja pasar sin redirigir", async () => {
    vi.mocked(claimsTokenFromCookieHeader).mockResolvedValue("cabecera.claims.");

    const respuesta = await middleware(new NextRequest(`${APP}/`, { headers: { cookie: "flit_session_impuestos=x" } }));

    expect(claimsTokenFromCookieHeader).toHaveBeenCalledWith("flit_session_impuestos=x", "impuestos");
    expect(respuesta.headers.get("location")).toBeNull();
    expect(respuesta.headers.get("x-middleware-next")).toBe("1");
  });

  it("la cookie de otro producto (diagnostico) no abre sesión en Impuestos", async () => {
    vi.mocked(claimsTokenFromCookieHeader).mockImplementation(async (_c, producto) => (producto === "diagnostico" ? "x" : null));

    const respuesta = await middleware(new NextRequest(`${APP}/reportes`, { headers: { cookie: "flit_session_diagnostico=x" } }));

    expect(respuesta.status).toBe(307);
    expect(new URL(respuesta.headers.get("location")!).pathname).toBe("/auth/login");
  });

  it("exige sesión en las pantallas y deja fuera login, API, salud, 403 y estáticos (sin bucles)", () => {
    for (const pantalla of ["/", "/liquidaciones", "/liquidaciones/nueva", "/reportes", "/usuarios", "/configuracion", "/backoffice/empresas"]) {
      expect(exigeSesion(pantalla), pantalla).toBe(true);
    }
    for (const libre of ["/auth/login", "/auth/callback", "/api/v1/impuestos/sesion", "/healthz", "/403", "/_next/static/x.js", "/assets/favicon.svg", "/icon.svg"]) {
      expect(exigeSesion(libre), libre).toBe(false);
    }
  });

  it("el login lleva al authorize del hub con PKCE S256 y client_id (aud) impuestos", async () => {
    const respuesta = await authRoutes.login(new Request(`${APP}/auth/login?returnTo=%2Fliquidaciones`));

    expect(respuesta.status).toBe(302);
    const destino = new URL(respuesta.headers.get("location")!);
    expect(destino.origin + destino.pathname).toBe(`${HUB}/connect/authorize`);
    expect(destino.searchParams.get("client_id")).toBe("impuestos");
    expect(destino.searchParams.get("response_type")).toBe("code");
    expect(destino.searchParams.get("code_challenge_method")).toBe("S256");
    expect(destino.searchParams.get("code_challenge")).toBeTruthy();
    expect(destino.searchParams.get("redirect_uri")).toBe(`${APP}/auth/callback`);
    expect(respuesta.headers.getSetCookie().some((c) => c.startsWith("flit_oidc_tx_impuestos="))).toBe(true);
  });
});

describe("regreso del hub", () => {
  it("tras autenticarse vuelve a la ruta pedida con la cookie HttpOnly flit_session_impuestos", async () => {
    const login = await authRoutes.login(new Request(`${APP}/auth/login?returnTo=%2Fliquidaciones`));
    const state = new URL(login.headers.get("location")!).searchParams.get("state")!;
    const accessToken = jwt({ sub: "u-1", aud: "impuestos", tenant_id: "t-1", exp: ahora() + 900 });
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ access_token: accessToken, refresh_token: "r-1", expires_in: 900 }));
    vi.stubGlobal("fetch", fetchMock);

    const respuesta = await authRoutes.callback(
      new Request(`${APP}/auth/callback?code=codigo-1&state=${state}`, { headers: { cookie: cabeceraCookie(login.headers.getSetCookie()) } }),
    );

    expect(respuesta.status).toBe(302);
    expect(respuesta.headers.get("location")).toBe("/liquidaciones");
    expect(fetchMock.mock.calls[0][0]).toBe(`${GATEWAY}/connect/token`);
    const sesion = respuesta.headers.getSetCookie().find((c) => c.startsWith("flit_session_impuestos"));
    expect(sesion).toBeDefined();
    expect(sesion).toContain("HttpOnly");
  });
});
