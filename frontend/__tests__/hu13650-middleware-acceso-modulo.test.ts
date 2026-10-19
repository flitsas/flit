import { afterEach, describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";
import { middleware } from "@/middleware";
import { accessDeniedPath } from "@/lib/auth/guard";
import { matchRouteRule, resolveRouteAccess } from "@/lib/nav/module-access";

// HU #13650 (Feature #13102, ADR-0072) — el corte de /admin/* y /empresa/* en el borde, con la tabla única de acceso:
// sin el permiso, al inicio con la marca de acceso denegado; con él, pasa sin aviso; sin sesión, /403.

afterEach(() => vi.unstubAllEnvs());

function jwt(payload: Record<string, unknown>): string {
  const b64 = (o: unknown) => Buffer.from(JSON.stringify(o)).toString("base64url");
  return `${b64({ alg: "none", typ: "JWT" })}.${b64({ sub: "u1", ...payload })}.sig`;
}

const request = (path: string, token?: string) =>
  new NextRequest(new URL(path, "https://dev.flitsas.online"), token ? { headers: { cookie: `flit_token=${token}` } } : undefined);

const location = (res: Response) => {
  const raw = res.headers.get("location");
  return raw ? new URL(raw) : null;
};

describe("middleware — acceso por URL a módulos sin permiso (modo heredado)", () => {
  it("AC2: /admin/* sin permiso redirige al inicio con la marca de acceso denegado", async () => {
    const res = await middleware(request("/admin/companies", jwt({ roles: [{ code: "gestor" }], permissions: [] })));
    const to = location(res);
    expect(to?.pathname).toBe("/");
    expect(to?.searchParams.get("acceso")).toBe("denegado");
    expect(to?.searchParams.get("modulo")).toBe("admin-companies");
  });

  it("AC2: /empresa/* sin el rol redirige al inicio con el mismo aviso", async () => {
    const res = await middleware(request("/empresa/usuarios", jwt({ roles: [{ code: "gestor" }] })));
    const to = location(res);
    expect(to?.pathname).toBe("/");
    expect(to?.searchParams.get("acceso")).toBe("denegado");
    expect(to?.searchParams.get("modulo")).toBe("empresa");
  });

  it("AC2: Generación documental sin `generacion-documental.read` redirige; con él pasa", async () => {
    const sin = await middleware(request("/admin/generacion-documental", jwt({ roles: [{ code: "AdminCompany" }], permissions: [] })));
    expect(location(sin)?.searchParams.get("acceso")).toBe("denegado");

    const con = await middleware(
      request("/admin/generacion-documental", jwt({ roles: [{ code: "AdminCompany" }], permissions: ["generacion-documental.read"] })),
    );
    expect(location(con)).toBeNull();
  });

  it("AC3: con el permiso/rol ve el módulo, sin aviso ni redirección", async () => {
    const superAdmin = await middleware(request("/admin/banners", jwt({ roles: [{ code: "SuperAdmin" }] })));
    expect(location(superAdmin)).toBeNull();

    const gestor = await middleware(request("/empresa/usuarios", jwt({ roles: [{ code: "AdminCompany" }] })));
    expect(location(gestor)).toBeNull();
  });

  it("sin sesión (o token vencido) sigue a /403: el inicio con aviso es solo para quien tiene sesión", async () => {
    const sinToken = await middleware(request("/admin/companies"));
    expect(location(sinToken)?.pathname).toBe("/403");

    const vencido = await middleware(request("/admin/companies", jwt({ roles: [{ code: "SuperAdmin" }], exp: 1 })));
    expect(location(vencido)?.pathname).toBe("/403");
  });

  it("el inicio y /tramites no los corta el middleware heredado", async () => {
    expect(location(await middleware(request("/", jwt({ roles: [] }))))).toBeNull();
    expect(location(await middleware(request("/tramites", jwt({ roles: [] }))))).toBeNull();
  });
});

describe("tabla de rutas (ROUTE_ACCESS)", () => {
  it("accessDeniedPath arma el destino uniforme", () => {
    expect(accessDeniedPath("reportes")).toBe("/?acceso=denegado&modulo=reportes");
    expect(accessDeniedPath()).toBe("/?acceso=denegado");
  });

  it("una ruta que ninguna regla cubre no la gobierna la tabla", () => {
    expect(matchRouteRule("/tramites")).toBeUndefined();
    expect(resolveRouteAccess(null, "/tramites")).toEqual({ allowed: true });
  });

  it("claims ilegibles niegan una ruta protegida (deny-by-default)", () => {
    expect(resolveRouteAccess(null, "/admin/jobs").allowed).toBe(false);
  });

  it("decodifica y baja a minúsculas: un segmento percent-encoded no abre un bypass", () => {
    expect(matchRouteRule("/admin/transit-offices/abc/%52ules")?.id).toBe("admin-transit-offices-config");
  });
});
