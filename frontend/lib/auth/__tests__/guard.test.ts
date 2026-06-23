// AC6 — Acceso restringido SuperAdmin. La lógica pura del gate se prueba sin el
// runtime de Next.js.
//
// Uso de ejemplo:
//   const { allowed } = evaluateAdminAccess(makeToken({ role: "SuperAdmin" }));
import { describe, expect, it } from "vitest";
import { evaluateAdminAccess, evaluateLoginAccess, FORBIDDEN_PATH, HOME_PATH } from "../guard";

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none", typ: "JWT" })).toString("base64url");
  const body = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `${header}.${body}.`;
}

describe("evaluateAdminAccess (AC6)", () => {
  it("permite el acceso a un SuperAdmin", () => {
    const decision = evaluateAdminAccess(makeToken({ sub: "u1", role: "SuperAdmin" }));
    expect(decision.allowed).toBe(true);
    expect(decision.redirectTo).toBeUndefined();
  });

  it("reconoce el rol en el arreglo roles (case-insensitive)", () => {
    const decision = evaluateAdminAccess(makeToken({ roles: ["operador", "superadmin"] }));
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

  it("deniega ot_admin fuera de /admin/transit-offices", () => {
    const decision = evaluateAdminAccess(
      makeToken({ sub: "u1", role: "ot_admin" }),
      "/admin/companies",
    );
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
