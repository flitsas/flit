// HU #13439 (Feature #13436, Épica #12750) — el gate de borde de /admin/banners es exclusivo del
// Super Admin FLIT. Antes (HU #12241) se abría por el permiso `banners.manage`; ahora ningún otro rol
// entra, ni siquiera con ese permiso en el token (el backend exige el rol SuperAdmin).
//
// Uso de ejemplo:
//   evaluateAdminAccess(token, "/admin/banners") → { allowed: true } solo si role === "SuperAdmin"
import { describe, expect, it } from "vitest";
import { evaluateAdminAccess, FORBIDDEN_PATH } from "../guard";
import { canManageBanners, decodeJwtPayload } from "../jwt";

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none", typ: "JWT" })).toString("base64url");
  const body = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `${header}.${body}.`;
}

const PATH = "/admin/banners";

describe("evaluateAdminAccess — /admin/banners (solo SuperAdmin)", () => {
  it("AC1: permite a SuperAdmin", () => {
    expect(evaluateAdminAccess(makeToken({ sub: "u1", role: "SuperAdmin" }), PATH).allowed).toBe(true);
  });

  it("AC3: redirige a /403 a un AdminCompany aunque tenga el permiso banners.manage", () => {
    const decision = evaluateAdminAccess(
      makeToken({ sub: "u1", role: "AdminCompany", permissions: ["banners.manage"] }),
      PATH,
    );
    expect(decision.allowed).toBe(false);
    expect(decision.redirectTo).toBe(FORBIDDEN_PATH);
  });

  it("AC3: redirige a /403 a un rol personalizado con el permiso banners.manage", () => {
    const decision = evaluateAdminAccess(
      makeToken({ sub: "u1", role: "Gerente", permissions: ["banners.manage"] }),
      PATH,
    );
    expect(decision.allowed).toBe(false);
    expect(decision.redirectTo).toBe(FORBIDDEN_PATH);
  });

  it("AC3: redirige a /403 a un AdminCompany sin el permiso", () => {
    const decision = evaluateAdminAccess(makeToken({ sub: "u1", role: "AdminCompany" }), PATH);
    expect(decision.allowed).toBe(false);
    expect(decision.redirectTo).toBe(FORBIDDEN_PATH);
  });

  it("AC3: también bloquea las subrutas de banners", () => {
    const token = makeToken({ sub: "u1", role: "AdminCompany", permissions: ["banners.manage"] });
    expect(evaluateAdminAccess(token, `${PATH}/nuevo`).allowed).toBe(false);
  });

  it("sin sesión activa no entra, aunque el path sea el del módulo", () => {
    expect(evaluateAdminAccess(null, PATH).allowed).toBe(false);
  });
});

describe("canManageBanners", () => {
  it("es true solo para SuperAdmin", () => {
    expect(canManageBanners(decodeJwtPayload(makeToken({ sub: "u1", role: "SuperAdmin" })))).toBe(true);
  });

  it("es false para AdminCompany con el permiso banners.manage", () => {
    const payload = decodeJwtPayload(
      makeToken({ sub: "u1", role: "AdminCompany", permissions: ["banners.manage"] }),
    );
    expect(canManageBanners(payload)).toBe(false);
  });

  it("es false sin payload", () => {
    expect(canManageBanners(null)).toBe(false);
  });
});
