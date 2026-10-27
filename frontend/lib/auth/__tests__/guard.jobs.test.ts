import { describe, expect, it } from "vitest";
import { evaluateAdminAccess } from "../guard";

// HU #13650: sesión válida sin el permiso → inicio con el aviso (no /403, que queda para fallos de sesión).
const ACCESO_DENEGADO = /^\/\?acceso=denegado/;

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none", typ: "JWT" })).toString("base64url");
  const body = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `${header}.${body}.`;
}

const PATH = "/admin/jobs/ict";

describe("evaluateAdminAccess — /admin/jobs", () => {
  it("permite SuperAdmin en el catálogo y en el formulario ICT", () => {
    const token = makeToken({ sub: "u1", role: "SuperAdmin" });
    expect(evaluateAdminAccess(token, "/admin/jobs").allowed).toBe(true);
    expect(evaluateAdminAccess(token, PATH).allowed).toBe(true);
  });

  it("bloquea AdminCompany y ot_admin", () => {
    expect(
      evaluateAdminAccess(makeToken({ sub: "u1", role: "AdminCompany" }), "/admin/jobs"),
    ).toEqual({ allowed: false, redirectTo: expect.stringMatching(ACCESO_DENEGADO) });
    expect(
      evaluateAdminAccess(makeToken({ sub: "u1", role: "AdminCompany" }), PATH),
    ).toEqual({ allowed: false, redirectTo: expect.stringMatching(ACCESO_DENEGADO) });
    expect(
      evaluateAdminAccess(makeToken({ sub: "u1", role: "ot_admin" }), PATH),
    ).toEqual({ allowed: false, redirectTo: expect.stringMatching(ACCESO_DENEGADO) });
  });

  it("bloquea sin token", () => {
    expect(evaluateAdminAccess(null, PATH).allowed).toBe(false);
  });
});
