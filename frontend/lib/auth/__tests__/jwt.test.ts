// HU #10506 (multi-rol) — el claim `roles` del JWT es un array de objetos {id, code},
// no de strings. Regresión: isSuperAdmin/isAdminCompany/isOtAdmin no deben asumir que
// cada elemento tiene `.toLowerCase()` propio (bug real detectado en pruebas manuales:
// "r?.toLowerCase is not a function" al iniciar sesión con un usuario multi-rol).
import { describe, expect, it } from "vitest";
import {
  canAdminResetPassword,
  isAdminCompany,
  isOtAdmin,
  isSuperAdmin,
  type JwtPayload,
} from "../jwt";

describe("isSuperAdmin / isAdminCompany / isOtAdmin — claim roles como array de objetos", () => {
  it("no lanza y detecta SuperAdmin cuando roles es [{id, code}, ...] con 2+ roles", () => {
    const payload: JwtPayload = {
      roles: [
        { id: "r1", code: "SuperAdmin" },
        { id: "r2", code: "AdminCompany" },
      ],
    };

    expect(() => isSuperAdmin(payload)).not.toThrow();
    expect(isSuperAdmin(payload)).toBe(true);
    expect(isAdminCompany(payload)).toBe(true);
    expect(isOtAdmin(payload)).toBe(false);
  });

  it("detecta ot_admin dentro de un array de roles multi-rol", () => {
    const payload: JwtPayload = {
      roles: [
        { id: "r1", code: "Radicador" },
        { id: "r2", code: "ot_admin" },
      ],
    };

    expect(() => isOtAdmin(payload)).not.toThrow();
    expect(isOtAdmin(payload)).toBe(true);
    expect(isSuperAdmin(payload)).toBe(false);
  });

  it("sigue funcionando con el fallback singular role_code para un usuario con un solo rol", () => {
    const payload: JwtPayload = { role_code: "SuperAdmin" };

    expect(isSuperAdmin(payload)).toBe(true);
    expect(isAdminCompany(payload)).toBe(false);
  });

  it("no lanza si algún elemento de roles viene sin code (dato inesperado)", () => {
    const payload = { roles: [{ id: "r1" }, null, { id: "r2", code: "AdminCompany" }] } as unknown as JwtPayload;

    expect(() => isAdminCompany(payload)).not.toThrow();
    expect(isAdminCompany(payload)).toBe(true);
  });

  it("retorna false sin lanzar si roles y role/role_code están ausentes", () => {
    expect(isSuperAdmin({})).toBe(false);
    expect(isAdminCompany({})).toBe(false);
    expect(isOtAdmin({})).toBe(false);
    expect(isSuperAdmin(null)).toBe(false);
  });
});

describe("canAdminResetPassword (HU-B auth-parity)", () => {
  it("permite SuperAdmin y AdminCompany", () => {
    expect(canAdminResetPassword({ role_code: "SuperAdmin" })).toBe(true);
    expect(canAdminResetPassword({ roles: [{ id: "1", code: "AdminCompany" }] })).toBe(true);
  });

  it("permite el permiso security.users.reset_password", () => {
    expect(
      canAdminResetPassword({ permissions: ["security.users.reset_password"] }),
    ).toBe(true);
  });

  it("niega a un operador sin rol ni permiso", () => {
    expect(canAdminResetPassword({ role_code: "Radicador", permissions: [] })).toBe(false);
  });
});
