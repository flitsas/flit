// Feature #14453 — quién usa el módulo Caja y quién solo ve Caja (el inicio lo lleva a /caja).
import { describe, expect, it } from "vitest";
import { canUseCaja, isOtCaja, isSoloCaja, roleCodesOf, type JwtPayload } from "../jwt";

const con = (...codes: string[]): JwtPayload => ({ roles: codes.map((code) => ({ id: code, code })) });

describe("roles de la caja", () => {
  it("ot_caja: usa la caja y SOLO ve la caja", () => {
    expect(isOtCaja(con("ot_caja"))).toBe(true);
    expect(canUseCaja(con("ot_caja"))).toBe(true);
    expect(isSoloCaja(con("ot_caja"))).toBe(true);
  });

  it("ot_admin: usa la caja pero no es solo-caja", () => {
    expect(canUseCaja(con("ot_admin"))).toBe(true);
    expect(isSoloCaja(con("ot_admin"))).toBe(false);
    expect(isSoloCaja(con("ot_admin", "ot_caja"))).toBe(false);
  });

  it("otros roles y el SuperAdmin no usan la caja", () => {
    expect(canUseCaja(con("gestor_ot"))).toBe(false);
    expect(canUseCaja(con("AdminCompany"))).toBe(false);
    expect(canUseCaja(con("SuperAdmin"))).toBe(false);
    expect(canUseCaja(null)).toBe(false);
    expect(isSoloCaja(null)).toBe(false);
  });

  it("lee role_code cuando no viene el arreglo roles, sin importar mayúsculas", () => {
    expect(roleCodesOf({ role_code: "OT_Caja" })).toEqual(["ot_caja"]);
    expect(isSoloCaja({ role: "ot_caja" })).toBe(true);
  });
});
