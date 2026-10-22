// HU #13665 (Feature #13636 F-F): Ver / Crear por familia desde el claim `permissions`, con la regla de
// compatibilidad para tokens anteriores a la migración #13663.
import { describe, expect, it } from "vitest";
import {
  canCreateFamily,
  canReadFamily,
  creatableFamilies,
  hasFamilyPermissions,
  readableFamilies,
} from "../familyPermissions";

const claims = (permissions: string[], roles: { code: string }[] = []) => ({ sub: "u1", roles, permissions });

describe("familyPermissions", () => {
  it("AC1: con traspasos.read y sin traspasos.create ve traspasos pero no puede crearlos", () => {
    const c = claims(["tramites.read", "tramites.create", "traspasos.read", "matriculas.read", "matriculas.create"]);
    expect(canReadFamily(c, "TRASPASO")).toBe(true);
    expect(canCreateFamily(c, "TRASPASO")).toBe(false);
    expect(canCreateFamily(c, "MATRICULAS")).toBe(true);
    // tramites.create no se hereda a las familias en cuanto el token trae permisos por familia.
    expect(canCreateFamily(c, "OTROS")).toBe(false);
  });

  it("AC2: sin matriculas.read no ve Matrículas pero sí las demás familias con .read", () => {
    const c = claims(["tramites.read", "traspasos.read", "otros-tramites.read"]);
    expect(canReadFamily(c, "MATRICULAS")).toBe(false);
    expect(readableFamilies(c)).toEqual(["TRASPASO", "OTROS"]);
    expect(creatableFamilies(c)).toEqual([]);
  });

  it("AC3: con .read y .create de las tres familias ve y crea todo", () => {
    const c = claims([
      "matriculas.read", "matriculas.create", "traspasos.read", "traspasos.create", "otros-tramites.read", "otros-tramites.create",
    ]);
    expect(readableFamilies(c)).toEqual(["MATRICULAS", "TRASPASO", "OTROS"]);
    expect(creatableFamilies(c)).toEqual(["MATRICULAS", "TRASPASO", "OTROS"]);
  });

  describe("compatibilidad: token anterior a la migración (ninguno de los 6 slugs)", () => {
    it("tramites.read / tramites.create equivalen a las tres familias", () => {
      const c = claims(["tramites.read", "tramites.create"]);
      expect(hasFamilyPermissions(c)).toBe(false);
      expect(readableFamilies(c)).toEqual(["MATRICULAS", "TRASPASO", "OTROS"]);
      expect(creatableFamilies(c)).toEqual(["MATRICULAS", "TRASPASO", "OTROS"]);
    });

    it("solo tramites.read: ve todo pero no crea nada", () => {
      const c = claims(["tramites.read"]);
      expect(readableFamilies(c)).toHaveLength(3);
      expect(creatableFamilies(c)).toEqual([]);
    });

    it("sin ninguno de los permisos no ve ni crea nada", () => {
      const c = claims(["reportes.read"]);
      expect(readableFamilies(c)).toEqual([]);
      expect(creatableFamilies(c)).toEqual([]);
    });
  });

  it("el SuperAdmin ve y crea todas las familias", () => {
    const c = claims([], [{ code: "SuperAdmin" }]);
    expect(readableFamilies(c)).toHaveLength(3);
    expect(creatableFamilies(c)).toHaveLength(3);
  });

  it("claims ilegibles: deny-by-default", () => {
    expect(canReadFamily(null, "OTROS")).toBe(false);
    expect(canCreateFamily(null, "OTROS")).toBe(false);
  });

  it("los slugs se comparan sin distinguir mayúsculas", () => {
    expect(canReadFamily(claims(["Traspasos.READ"]), "TRASPASO")).toBe(true);
  });
});
