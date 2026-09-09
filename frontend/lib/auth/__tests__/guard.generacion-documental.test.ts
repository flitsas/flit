// HU-01 (Feature #12201) — el gate de borde de /admin/generacion-documental se resuelve por
// el permiso `generacion-documental.read`, no por rol: sin esto un AdminCompany con el
// módulo habilitado sería redirigido a /403 antes de renderizar nada.
//
// Uso de ejemplo:
//   evaluateAdminAccess(token, "/admin/generacion-documental") → { allowed: true }
import { describe, expect, it } from "vitest";
import { evaluateAdminAccess, FORBIDDEN_PATH } from "../guard";

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none", typ: "JWT" })).toString("base64url");
  const body = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `${header}.${body}.`;
}

const PATH = "/admin/generacion-documental";

describe("evaluateAdminAccess — /admin/generacion-documental", () => {
  it("permite a un AdminCompany con el permiso generacion-documental.read", () => {
    const token = makeToken({
      sub: "u1",
      role: "AdminCompany",
      permissions: ["generacion-documental.read", "generacion-documental.generate"],
    });
    expect(evaluateAdminAccess(token, PATH).allowed).toBe(true);
  });

  it("permite a SuperAdmin (bypass de permisos, igual que el backend)", () => {
    expect(evaluateAdminAccess(makeToken({ sub: "u1", role: "SuperAdmin" }), PATH).allowed).toBe(true);
  });

  it("redirige a /403 a un AdminCompany SIN el permiso del módulo", () => {
    const decision = evaluateAdminAccess(
      makeToken({ sub: "u1", role: "AdminCompany", permissions: ["security.users.read"] }),
      PATH,
    );
    expect(decision.allowed).toBe(false);
    expect(decision.redirectTo).toBe(FORBIDDEN_PATH);
  });

  it("no abre otras rutas admin por tener el permiso del módulo", () => {
    const token = makeToken({
      sub: "u1",
      role: "AdminCompany",
      permissions: ["generacion-documental.read"],
    });
    expect(evaluateAdminAccess(token, "/admin/quipux").allowed).toBe(false);
  });

  it("sin sesión activa no entra, aunque el path sea el del módulo", () => {
    expect(evaluateAdminAccess(null, PATH).allowed).toBe(false);
  });
});
