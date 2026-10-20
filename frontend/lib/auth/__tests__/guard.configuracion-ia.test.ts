// HU #13511 AC3 — /admin/plataforma/configuracion-ia es exclusivo de Super Admin.
import { describe, expect, it } from "vitest";
import { evaluateAdminAccess, FORBIDDEN_PATH } from "../guard";

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none", typ: "JWT" })).toString("base64url");
  const body = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `${header}.${body}.`;
}

const PATH = "/admin/plataforma/configuracion-ia";

describe("evaluateAdminAccess — /admin/plataforma/configuracion-ia (HU #13511)", () => {
  it("el Super Admin entra", () => {
    expect(evaluateAdminAccess(makeToken({ sub: "u1", role: "SuperAdmin" }), PATH).allowed).toBe(true);
  });

  it("el Admin de Compañía no entra y va a acceso denegado", () => {
    const decision = evaluateAdminAccess(makeToken({ sub: "u1", role: "AdminCompany" }), PATH);
    expect(decision.allowed).toBe(false);
    expect(decision.redirectTo).toBe(FORBIDDEN_PATH);
  });

  it("el Admin OT no entra y va a acceso denegado", () => {
    const decision = evaluateAdminAccess(makeToken({ sub: "u1", role: "ot_admin" }), PATH);
    expect(decision.allowed).toBe(false);
    expect(decision.redirectTo).toBe(FORBIDDEN_PATH);
  });
});
