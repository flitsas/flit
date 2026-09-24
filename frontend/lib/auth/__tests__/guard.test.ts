// AC6 — Acceso restringido SuperAdmin. La lógica pura del gate se prueba sin el
// runtime de Next.js.
//
// Uso de ejemplo:
//   const { allowed } = evaluateAdminAccess(makeToken({ role: "SuperAdmin" }));
import { describe, expect, it } from "vitest";
import {
  evaluateAdminAccess,
  evaluateEmpresaAccess,
  evaluateLoginAccess,
  getUserRole,
  FORBIDDEN_PATH,
  HOME_PATH,
} from "../guard";

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none", typ: "JWT" })).toString("base64url");
  const body = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `${header}.${body}.`;
}

const PAST_EXP = Math.floor(Date.now() / 1000) - 3600;

describe("evaluateAdminAccess (AC6)", () => {
  it("permite el acceso a un SuperAdmin", () => {
    const decision = evaluateAdminAccess(makeToken({ sub: "u1", role: "SuperAdmin" }));
    expect(decision.allowed).toBe(true);
    expect(decision.redirectTo).toBeUndefined();
  });

  it("reconoce el rol en el arreglo roles (case-insensitive, HU #10506: objetos {id, code})", () => {
    const decision = evaluateAdminAccess(
      makeToken({
        roles: [
          { id: "r1", code: "operador" },
          { id: "r2", code: "superadmin" },
        ],
      }),
    );
    expect(decision.allowed).toBe(true);
  });

  it("redirige a /403 cuando el rol no es SuperAdmin", () => {
    const decision = evaluateAdminAccess(makeToken({ role: "Operador" }));
    expect(decision.allowed).toBe(false);
    expect(decision.redirectTo).toBe(FORBIDDEN_PATH);
  });

  it("redirige a /403 cuando no hay token", () => {
    const decision = evaluateAdminAccess(undefined);
    expect(decision.allowed).toBe(false);
    expect(decision.redirectTo).toBe(FORBIDDEN_PATH);
  });

  it("redirige a /403 cuando el token está malformado", () => {
    const decision = evaluateAdminAccess("no-es-un-jwt");
    expect(decision.allowed).toBe(false);
    expect(decision.redirectTo).toBe(FORBIDDEN_PATH);
  });

  it("permite ot_admin en rutas /admin/transit-offices (HU #10218)", () => {
    const decision = evaluateAdminAccess(
      makeToken({ sub: "u1", role: "ot_admin" }),
      "/admin/transit-offices/abc/tramites",
    );
    expect(decision.allowed).toBe(true);
  });

  it("permite ot_admin en listado /admin/transit-offices (HU #10236)", () => {
    const decision = evaluateAdminAccess(
      makeToken({ sub: "u1", role: "ot_admin" }),
      "/admin/transit-offices",
    );
    expect(decision.allowed).toBe(true);
  });

  it("permite a un rol no-ot_admin de un tenant OT en /admin/transit-offices", () => {
    const decision = evaluateAdminAccess(
      makeToken({ sub: "u1", role: "gestor_ot", entity_type: "TRANSIT_OFFICE" }),
      "/admin/transit-offices/abc/client-procedures",
    );
    expect(decision.allowed).toBe(true);
  });

  it("deniega a un rol de empresa (COMPANY) en /admin/transit-offices", () => {
    const decision = evaluateAdminAccess(
      makeToken({ sub: "u1", role: "gestor_ot", entity_type: "COMPANY" }),
      "/admin/transit-offices/abc/client-procedures",
    );
    expect(decision.allowed).toBe(false);
  });

  it("deniega ot_admin fuera de /admin/transit-offices", () => {
    const decision = evaluateAdminAccess(
      makeToken({ sub: "u1", role: "ot_admin" }),
      "/admin/companies",
    );
    expect(decision.allowed).toBe(false);
    expect(decision.redirectTo).toBe(FORBIDDEN_PATH);
  });

  it("permite AdminCompany en /admin/companies (HU #11228)", () => {
    const decision = evaluateAdminAccess(
      makeToken({ sub: "u1", role: "AdminCompany", tenant_id: "t1" }),
      "/admin/companies/t1",
    );
    expect(decision.allowed).toBe(true);
  });

  it("deniega AdminCompany fuera de /admin/companies (HU #11228)", () => {
    const decision = evaluateAdminAccess(
      makeToken({ sub: "u1", role: "AdminCompany", tenant_id: "t1" }),
      "/admin/transit-offices",
    );
    expect(decision.allowed).toBe(false);
    expect(decision.redirectTo).toBe(FORBIDDEN_PATH);
  });

  it("redirige a /403 cuando el token de SuperAdmin ya expiró", () => {
    const decision = evaluateAdminAccess(makeToken({ role: "SuperAdmin", exp: PAST_EXP }));
    expect(decision.allowed).toBe(false);
    expect(decision.redirectTo).toBe(FORBIDDEN_PATH);
  });

  it("redirige a /403 cuando el token de ot_admin ya expiró", () => {
    const decision = evaluateAdminAccess(
      makeToken({ role: "ot_admin", exp: PAST_EXP }),
      "/admin/transit-offices",
    );
    expect(decision.allowed).toBe(false);
    expect(decision.redirectTo).toBe(FORBIDDEN_PATH);
  });

  // HU12856 AC2 — Reglas, Requisitos y Configuración del organismo quedan exclusivos de
  // Super Admin también en la capa de UI (bloqueo en dos capas: la API ya exige SuperAdminPolicy).
  it.each([
    ["/admin/transit-offices/abc/rules", "Reglas"],
    ["/admin/transit-offices/abc/requirements", "Requisitos"],
    ["/admin/transit-offices/abc/configuracion", "Configuración"],
  ])("HU12856 AC2 — deniega a ot_admin en %s (%s)", (pathname: string) => {
    const decision = evaluateAdminAccess(makeToken({ sub: "u1", role: "ot_admin" }), pathname);
    expect(decision.allowed).toBe(false);
    expect(decision.redirectTo).toBe(FORBIDDEN_PATH);
  });

  it("HU12856 AC2 — deniega a cualquier rol de un tenant OT (no solo ot_admin) en /rules", () => {
    const decision = evaluateAdminAccess(
      makeToken({ sub: "u1", role: "gestor_tramites_ot", entity_type: "TRANSIT_OFFICE" }),
      "/admin/transit-offices/abc/requirements",
    );
    expect(decision.allowed).toBe(false);
    expect(decision.redirectTo).toBe(FORBIDDEN_PATH);
  });

  it("HU12856 AC3 — un Super Admin conserva acceso a /rules, /requirements y /configuracion", () => {
    for (const segment of ["rules", "requirements", "configuracion"]) {
      const decision = evaluateAdminAccess(
        makeToken({ sub: "u1", role: "SuperAdmin" }),
        `/admin/transit-offices/abc/${segment}`,
      );
      expect(decision.allowed).toBe(true);
    }
  });

  it("HU12856 — un ot_admin conserva acceso al resto de /admin/transit-offices/* (client-procedures, documents…)", () => {
    const decision = evaluateAdminAccess(
      makeToken({ sub: "u1", role: "ot_admin" }),
      "/admin/transit-offices/abc/documents",
    );
    expect(decision.allowed).toBe(true);
  });

  it("permite el acceso de SuperAdmin a /admin/improntas (HU #10469 AC1)", () => {
    const decision = evaluateAdminAccess(
      makeToken({ sub: "u1", role: "SuperAdmin" }),
      "/admin/improntas",
    );
    expect(decision.allowed).toBe(true);
  });

  it("deniega el acceso a /admin/improntas para un rol distinto de SuperAdmin (HU #10469 AC2)", () => {
    const decision = evaluateAdminAccess(
      makeToken({ sub: "u1", role: "ot_admin" }),
      "/admin/improntas",
    );
    expect(decision.allowed).toBe(false);
    expect(decision.redirectTo).toBe(FORBIDDEN_PATH);
  });
});

describe("getUserRole (refactor adminOT)", () => {
  it("devuelve 'superadmin' para un token SuperAdmin", () => {
    expect(getUserRole(makeToken({ role: "SuperAdmin" }))).toBe("superadmin");
  });

  it("devuelve 'admincompany' para un token AdminCompany", () => {
    expect(getUserRole(makeToken({ role: "AdminCompany" }))).toBe("admincompany");
  });

  it("devuelve 'ot_admin' para un token ot_admin", () => {
    expect(getUserRole(makeToken({ sub: "u1", role: "ot_admin" }))).toBe("ot_admin");
  });

  it("devuelve 'ot_admin' para cualquier rol de un tenant TRANSIT_OFFICE", () => {
    expect(
      getUserRole(makeToken({ sub: "u1", role: "gestor_ot", entity_type: "TRANSIT_OFFICE" })),
    ).toBe("ot_admin");
  });

  it("devuelve 'user' para un rol desconocido o sin token", () => {
    expect(getUserRole(makeToken({ role: "Operador" }))).toBe("user");
    expect(getUserRole(undefined)).toBe("user");
  });
});

describe("evaluateEmpresaAccess", () => {
  it("permite el acceso a AdminCompany", () => {
    const decision = evaluateEmpresaAccess(makeToken({ role: "AdminCompany" }));
    expect(decision.allowed).toBe(true);
  });

  it("redirige a /403 cuando no hay token", () => {
    const decision = evaluateEmpresaAccess(undefined);
    expect(decision.allowed).toBe(false);
    expect(decision.redirectTo).toBe(FORBIDDEN_PATH);
  });

  it("redirige a /403 cuando el token de AdminCompany ya expiró", () => {
    const decision = evaluateEmpresaAccess(makeToken({ role: "AdminCompany", exp: PAST_EXP }));
    expect(decision.allowed).toBe(false);
    expect(decision.redirectTo).toBe(FORBIDDEN_PATH);
  });
});

describe("evaluateLoginAccess (sesión activa → dashboard)", () => {
  const future = Math.floor(Date.now() / 1000) + 3600;
  const past = Math.floor(Date.now() / 1000) - 3600;

  it("redirige al dashboard cuando hay sesión activa (token vigente)", () => {
    const decision = evaluateLoginAccess(makeToken({ sub: "u1", exp: future }));
    expect(decision.redirect).toBe(true);
    expect(decision.redirectTo).toBe(HOME_PATH);
  });

  it("permite el login cuando no hay token", () => {
    const decision = evaluateLoginAccess(undefined);
    expect(decision.redirect).toBe(false);
    expect(decision.redirectTo).toBeUndefined();
  });

  it("permite el login cuando el token expiró", () => {
    const decision = evaluateLoginAccess(makeToken({ sub: "u1", exp: past }));
    expect(decision.redirect).toBe(false);
  });

  it("permite el login cuando el token está malformado", () => {
    const decision = evaluateLoginAccess("no-es-un-jwt");
    expect(decision.redirect).toBe(false);
  });
});
