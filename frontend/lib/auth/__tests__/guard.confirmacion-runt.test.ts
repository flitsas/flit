// HU #12313 (Feature #12276) — el gate de borde de /admin/plataforma/confirmacion-runt se abre con
// CUALQUIERA de los dos permisos del submódulo, y el resto de Plataforma sigue siendo SuperAdmin.
//
// Uso de ejemplo:
//   evaluateAdminAccess(token, "/admin/plataforma/confirmacion-runt/historial") → { allowed: true }
import { describe, expect, it } from "vitest";
import { evaluateAdminAccess, FORBIDDEN_PATH, RUNT_CONFIRMATION_BASE_PATH } from "../guard";

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none", typ: "JWT" })).toString("base64url");
  const body = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `${header}.${body}.`;
}

describe("evaluateAdminAccess — /admin/plataforma/confirmacion-runt", () => {
  it("permite a un rol de plataforma con solo runt_confirmation.history.read", () => {
    const token = makeToken({
      sub: "u1",
      role: "Auditor",
      permissions: ["runt_confirmation.history.read"],
    });
    expect(evaluateAdminAccess(token, `${RUNT_CONFIRMATION_BASE_PATH}/historial`).allowed).toBe(true);
  });

  it("permite a un rol con solo runt_confirmation.settings.manage", () => {
    const token = makeToken({
      sub: "u1",
      role: "AdminCompany",
      permissions: ["runt_confirmation.settings.manage"],
    });
    expect(evaluateAdminAccess(token, `${RUNT_CONFIRMATION_BASE_PATH}/configuracion`).allowed).toBe(true);
  });

  it("permite a SuperAdmin (bypass de permisos, igual que el backend)", () => {
    expect(
      evaluateAdminAccess(makeToken({ sub: "u1", role: "SuperAdmin" }), RUNT_CONFIRMATION_BASE_PATH).allowed,
    ).toBe(true);
  });

  it("redirige a /403 a un usuario SIN ninguno de los dos permisos", () => {
    const decision = evaluateAdminAccess(
      makeToken({ sub: "u1", role: "AdminCompany", permissions: ["security.users.read"] }),
      RUNT_CONFIRMATION_BASE_PATH,
    );
    expect(decision.allowed).toBe(false);
    expect(decision.redirectTo).toBe(FORBIDDEN_PATH);
  });

  it("no abre el resto de Plataforma por tener los permisos del submódulo", () => {
    const token = makeToken({
      sub: "u1",
      role: "AdminCompany",
      permissions: ["runt_confirmation.settings.manage", "runt_confirmation.history.read"],
    });
    expect(evaluateAdminAccess(token, "/admin/plataforma/tipos-tramite").allowed).toBe(false);
    expect(evaluateAdminAccess(token, "/admin/plataforma/mandatos").allowed).toBe(false);
  });

  it("sin sesión activa no entra, aunque el path sea el del submódulo", () => {
    expect(evaluateAdminAccess(null, RUNT_CONFIRMATION_BASE_PATH).allowed).toBe(false);
  });
});
