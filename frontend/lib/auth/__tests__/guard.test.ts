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

// HU #13650: sesión válida sin el permiso → inicio con el aviso (no /403, que queda para fallos de sesión).
const ACCESO_DENEGADO = /^\/\?acceso=denegado/;

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

  it("redirige al inicio con aviso cuando el rol no es SuperAdmin", () => {
    const decision = evaluateAdminAccess(makeToken({ role: "Operador" }));
    expect(decision.allowed).toBe(false);
    expect(decision.redirectTo).toMatch(ACCESO_DENEGADO);
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

  it("permite a un rol no-ot_admin de un tenant OT con ot.tramites.manage en la bandeja (HU #13655)", () => {
    const decision = evaluateAdminAccess(
      makeToken({ sub: "u1", role: "gestor_ot", entity_type: "TRANSIT_OFFICE", permissions: ["ot.tramites.manage"] }),
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
    expect(decision.redirectTo).toMatch(ACCESO_DENEGADO);
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
    expect(decision.redirectTo).toMatch(ACCESO_DENEGADO);
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
    expect(decision.redirectTo).toMatch(ACCESO_DENEGADO);
  });

  it("HU12856 AC2 — deniega a cualquier rol de un tenant OT (no solo ot_admin) en /rules", () => {
    const decision = evaluateAdminAccess(
      makeToken({ sub: "u1", role: "gestor_tramites_ot", entity_type: "TRANSIT_OFFICE" }),
      "/admin/transit-offices/abc/requirements",
    );
    expect(decision.allowed).toBe(false);
    expect(decision.redirectTo).toMatch(ACCESO_DENEGADO);
  });

  // Security review (Low) HU12856 — isSuperAdminOnlyOtRoute debe ser insensible a mayúsculas y
  // robusto ante segmentos percent-encoded: ninguna de estas variantes debe colarse a ot_admin.
  it.each([
    ["/admin/transit-offices/abc/Rules", "mayúscula simple"],
    ["/admin/transit-offices/abc/REQUIREMENTS/", "mayúsculas + slash final"],
    ["/admin/transit-offices/abc/configuracion", "configuracion (pathname sin query: Next.js le quita el ?x=1 antes de llegar aquí)"],
    ["/admin/transit-offices/abc/%72ules", "segmento percent-encoded (%72 = 'r')"],
  ])("deniega a ot_admin en %s (%s)", (pathname: string) => {
    const decision = evaluateAdminAccess(makeToken({ sub: "u1", role: "ot_admin" }), pathname);
    expect(decision.allowed).toBe(false);
    expect(decision.redirectTo).toMatch(ACCESO_DENEGADO);
  });

  it.each([
    "/admin/transit-offices/abc/Rules",
    "/admin/transit-offices/abc/REQUIREMENTS/",
    "/admin/transit-offices/abc/%72ules",
  ])("un Super Admin conserva acceso incluso a variantes mayúsculas/percent-encoded: %s", (pathname: string) => {
    const decision = evaluateAdminAccess(makeToken({ sub: "u1", role: "SuperAdmin" }), pathname);
    expect(decision.allowed).toBe(true);
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

  it("HU12856 — un ot_admin conserva acceso al resto de /admin/transit-offices/* (client-procedures)", () => {
    const decision = evaluateAdminAccess(
      makeToken({ sub: "u1", role: "ot_admin" }),
      "/admin/transit-offices/abc/client-procedures",
    );
    expect(decision.allowed).toBe(true);
  });

  // HU12860 AC (Feature #12848) — Documentos queda exclusivo de ot_admin y Super Admin también
  // en la capa de UI (bloqueo en dos capas: la API ya exige la policy OtAdminOrSuperAdmin).
  it("HU12860 — un ot_admin conserva acceso a /admin/transit-offices/{id}/documents", () => {
    const decision = evaluateAdminAccess(
      makeToken({ sub: "u1", role: "ot_admin" }),
      "/admin/transit-offices/abc/documents",
    );
    expect(decision.allowed).toBe(true);
  });

  it("HU12860 — un Super Admin conserva acceso a /admin/transit-offices/{id}/documents", () => {
    const decision = evaluateAdminAccess(
      makeToken({ sub: "u1", role: "SuperAdmin" }),
      "/admin/transit-offices/abc/documents",
    );
    expect(decision.allowed).toBe(true);
  });

  it.each([
    ["gestor_tramites_ot", "Operador OT"],
    ["otro_rol_ot", "rol OT personalizado"],
  ])(
    "HU12860 — deniega a %s (%s) en /admin/transit-offices/{id}/documents",
    (role: string) => {
      const decision = evaluateAdminAccess(
        makeToken({ sub: "u1", role, entity_type: "TRANSIT_OFFICE" }),
        "/admin/transit-offices/abc/documents",
      );
      expect(decision.allowed).toBe(false);
      expect(decision.redirectTo).toMatch(ACCESO_DENEGADO);
    },
  );

  // HU #13655 (Anexo A §1.4): la prelación documental se delega por permiso al Gestor OT.
  it("HU13655 — un Gestor OT con ot.documentos.manage entra a /admin/transit-offices/{id}/documents", () => {
    const decision = evaluateAdminAccess(
      makeToken({
        sub: "u1",
        role: "gestor_tramites_ot",
        entity_type: "TRANSIT_OFFICE",
        permissions: ["ot.tramites.manage", "ot.documentos.manage"],
      }),
      "/admin/transit-offices/abc/documents",
    );
    expect(decision.allowed).toBe(true);
  });

  it("HU13655 — el permiso de documentos no abre la ruta a quien no es de un organismo", () => {
    const decision = evaluateAdminAccess(
      makeToken({ sub: "u1", role: "Radicador", permissions: ["ot.documentos.manage"] }),
      "/admin/transit-offices/abc/documents",
    );
    expect(decision.allowed).toBe(false);
  });

  it("HU12860 — un Operador OT con su permiso de trámites conserva acceso a la bandeja (client-procedures)", () => {
    const decision = evaluateAdminAccess(
      makeToken({ sub: "u1", role: "gestor_tramites_ot", entity_type: "TRANSIT_OFFICE", permissions: ["ot.tramites.manage"] }),
      "/admin/transit-offices/abc/client-procedures",
    );
    expect(decision.allowed).toBe(true);
  });

  // HU #13655 — cada pestaña del organismo abre a un rol OT no administrador solo con su permiso OT.
  it.each([
    ["client-procedures", "ot.tramites.manage"],
    ["revocation-requests", "ot.tramites.manage"],
    ["usuarios", "ot.usuarios.manage"],
    ["reportes", "ot.reportes.manage"],
    ["mandatos", "ot.mandatos.manage"],
    ["imprint-validation", "ot.improntas.manage"],
    ["webhooks", "ot.integracion.manage"],
  ])("HU13655 — un Gestor OT entra a /admin/transit-offices/{id}/%s solo con %s", (segment: string, slug: string) => {
    const token = (permissions: string[]) =>
      makeToken({ sub: "u1", role: "gestor_tramites_ot", entity_type: "TRANSIT_OFFICE", permissions });
    const path = `/admin/transit-offices/abc/${segment}`;
    expect(evaluateAdminAccess(token([slug]), path).allowed).toBe(true);
    const denied = evaluateAdminAccess(token(["ot.documentos.manage"]), path);
    expect(denied.allowed).toBe(false);
    expect(denied.redirectTo).toMatch(ACCESO_DENEGADO);
  });

  it("HU13655 — el Inicio del organismo sigue abierto a cualquier rol OT y el ot_admin entra a todas las pestañas", () => {
    const gestor = makeToken({ sub: "u1", role: "gestor_tramites_ot", entity_type: "TRANSIT_OFFICE" });
    expect(evaluateAdminAccess(gestor, "/admin/transit-offices/abc").allowed).toBe(true);
    expect(evaluateAdminAccess(gestor, "/admin/transit-offices").allowed).toBe(true);
    const admin = makeToken({ sub: "u1", role: "ot_admin" });
    for (const segment of ["client-procedures", "usuarios", "reportes", "mandatos", "imprint-validation", "webhooks"]) {
      expect(evaluateAdminAccess(admin, `/admin/transit-offices/abc/${segment}`).allowed).toBe(true);
    }
  });

  // Security review (Low) HU12856 — mismo blindaje case-insensitive/percent-encoding para el
  // segmento nuevo (HU12860): ninguna variante debe colarse a un rol OT distinto de ot_admin.
  it.each([
    ["/admin/transit-offices/abc/Documents", "mayúscula simple"],
    ["/admin/transit-offices/abc/DOCUMENTS/", "mayúsculas + slash final"],
    ["/admin/transit-offices/abc/%64ocuments", "segmento percent-encoded (%64 = 'd')"],
  ])("HU12860 — deniega a un Operador OT en %s (%s)", (pathname: string) => {
    const decision = evaluateAdminAccess(
      makeToken({ sub: "u1", role: "gestor_tramites_ot", entity_type: "TRANSIT_OFFICE" }),
      pathname,
    );
    expect(decision.allowed).toBe(false);
    expect(decision.redirectTo).toMatch(ACCESO_DENEGADO);
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
    expect(decision.redirectTo).toMatch(ACCESO_DENEGADO);
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
