import { afterEach, describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";
import { middleware } from "@/middleware";

// El access token de la sesión de @flit/auth dura 15 minutos y lo renueva el servidor en la siguiente llamada. Volver a
// una pantalla de administración tras un rato sin actividad no es «sin sesión»: antes daba /403 con «Acceso restringido».
const claimsToken = vi.hoisted(() => vi.fn<() => string | null>());
vi.mock("@flit/auth/request", () => ({
  isOidcSessionMode: () => process.env.FLIT_SESSION_MODE === "oidc",
  claimsTokenFromCookieHeader: async () => claimsToken(),
}));

const HACE_UNA_HORA = Math.floor(Date.now() / 1000) - 3600;
const unsigned = (payload: object) =>
  `${btoa(JSON.stringify({ alg: "RS256" }))}.${btoa(JSON.stringify(payload)).replace(/=+$/, "").replace(/\+/g, "-").replace(/\//g, "_")}.`;
const request = (path: string) => new NextRequest(new URL(path, "https://tramites.cliente.com"));

afterEach(() => {
  vi.unstubAllEnvs();
  claimsToken.mockReset();
});

describe("token vencido con la sesión de @flit/auth", () => {
  it("el administrador de la empresa entra a /admin/companies", async () => {
    vi.stubEnv("FLIT_SESSION_MODE", "oidc");
    claimsToken.mockReturnValue(unsigned({ exp: HACE_UNA_HORA, roles: [{ id: "r1", code: "AdminCompany" }], permissions: [] }));

    const response = await middleware(request("/admin/companies/abc/children"));

    expect(response.headers.get("location")).toBeNull();
  });

  it("sin el rol sigue sin entrar: vuelve al inicio con el aviso, no a /403", async () => {
    vi.stubEnv("FLIT_SESSION_MODE", "oidc");
    claimsToken.mockReturnValue(unsigned({ exp: HACE_UNA_HORA, roles: [{ id: "r2", code: "Radicador" }], permissions: [] }));

    const response = await middleware(request("/admin/companies/abc/children"));

    expect(response.headers.get("location")).toContain("/?acceso=denegado");
  });

  it("sin sesión sigue yendo al login", async () => {
    vi.stubEnv("FLIT_SESSION_MODE", "oidc");
    claimsToken.mockReturnValue(null);

    const response = await middleware(request("/admin/companies"));

    expect(response.headers.get("location")).toContain("/auth/login?returnTo=");
  });
});

describe("token vencido con la sesión antigua", () => {
  it("sigue dando /403", async () => {
    const expired = unsigned({ exp: HACE_UNA_HORA, roles: [{ id: "r1", code: "AdminCompany" }], permissions: [] });
    const legacy = new NextRequest(new URL("/admin/companies", "https://dev.flitsas.online"), {
      headers: { cookie: `flit_token=${expired}` },
    });

    const response = await middleware(legacy);

    expect(response.headers.get("location")).toContain("/403");
  });
});
