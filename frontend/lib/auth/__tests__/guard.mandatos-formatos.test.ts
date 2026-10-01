// HU #13175 (Feature #13118) — la pantalla de formatos de contrato vive en /admin/plataforma/mandatos:
// solo el Super Admin entra. Admin OT y Admin de Compañía no la abren (la URL directa va a /403).
import { describe, expect, it } from "vitest";
import { evaluateAdminAccess, FORBIDDEN_PATH } from "../guard";

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none", typ: "JWT" })).toString("base64url");
  const body = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `${header}.${body}.`;
}

const PATH = "/admin/plataforma/mandatos";

describe("evaluateAdminAccess — /admin/plataforma/mandatos (formatos de contrato)", () => {
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
