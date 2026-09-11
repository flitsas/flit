import { describe, expect, it } from "vitest";
import { evaluateAdminAccess, FORBIDDEN_PATH } from "../guard";

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none", typ: "JWT" })).toString("base64url");
  const body = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `${header}.${body}.`;
}

const PATH = "/admin/jobs/ict";

describe("evaluateAdminAccess — /admin/jobs/ict", () => {
  it("permite SuperAdmin", () => {
    const token = makeToken({ sub: "u1", role: "SuperAdmin" });
    expect(evaluateAdminAccess(token, PATH).allowed).toBe(true);
  });

  it("bloquea AdminCompany y ot_admin", () => {
    expect(
      evaluateAdminAccess(makeToken({ sub: "u1", role: "AdminCompany" }), PATH),
    ).toEqual({ allowed: false, redirectTo: FORBIDDEN_PATH });
    expect(
      evaluateAdminAccess(makeToken({ sub: "u1", role: "ot_admin" }), PATH),
    ).toEqual({ allowed: false, redirectTo: FORBIDDEN_PATH });
  });

  it("bloquea sin token", () => {
    expect(evaluateAdminAccess(null, PATH).allowed).toBe(false);
  });
});
