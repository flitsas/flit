import { describe, expect, it } from "vitest";
import { findFamilyPair, familyRole, isFamilyCreateRequiresReadError, isServiceTypeModule, normalizeFamilyPairs, sortServiceTypeModules, toggleRolePermission } from "../familyPairs";

const modules = [
  { actions: [{ id: "r", slug: "matriculas.read" }, { id: "c", slug: "matriculas.create" }] },
  { actions: [{ id: "x", slug: "reportes.read" }] },
];

describe("familyPairs (HU #13666)", () => {
  it("encuentra el par solo en módulos de familia", () => {
    expect(findFamilyPair(modules[0].actions)?.create.id).toBe("c");
    expect(findFamilyPair(modules[1].actions)).toBeNull();
    expect(familyRole("traspasos.create")).toBe("create");
    expect(familyRole("otros-tramites.read")).toBe("read");
    expect(familyRole("tramites.read")).toBeNull();
  });

  it("apagar Ver apaga Crear; encender Crear sin Ver no hace nada; encender Ver no enciende Crear", () => {
    expect([...toggleRolePermission(new Set(["r", "c"]), "r", modules)]).toEqual([]);
    expect([...toggleRolePermission(new Set(), "c", modules)]).toEqual([]);
    expect([...toggleRolePermission(new Set(), "r", modules)]).toEqual(["r"]);
    expect([...toggleRolePermission(new Set(["r"]), "c", modules)].sort()).toEqual(["c", "r"]);
    expect([...toggleRolePermission(new Set(["r", "c"]), "c", modules)]).toEqual(["r"]);
    expect([...toggleRolePermission(new Set(["x"]), "x", modules)]).toEqual([]);
  });

  it("normaliza: ningún Crear sin su Ver", () => {
    expect([...normalizeFamilyPairs(new Set(["c", "x"]), modules)]).toEqual(["x"]);
  });

  it("reconoce el 400 por código en el cuerpo o en el mensaje", () => {
    expect(isFamilyCreateRequiresReadError({ body: { code: "FAMILY_CREATE_REQUIRES_READ" } })).toBe(true);
    expect(isFamilyCreateRequiresReadError(new Error('400: {"code":"FAMILY_CREATE_REQUIRES_READ"}'))).toBe(true);
    expect(isFamilyCreateRequiresReadError(new Error("boom"))).toBe(false);
  });
});

describe("isServiceTypeModule (HU #13653)", () => {
  it("reconoce los módulos de tipo de servicio por código o por su par Ver / Crear", () => {
    expect(isServiceTypeModule({ code: "tramites-matriculas", actions: [] })).toBe(true);
    expect(isServiceTypeModule({ code: "dashboard", actions: [{ id: "a", slug: "dashboard.read" }] })).toBe(false);
  });
});

describe("sortServiceTypeModules (HU #13653)", () => {
  it("ordena siempre Matrículas, Traspasos, Otros trámites; lo no reconocido va al final", () => {
    const list = [
      { code: "tramites-otros", actions: [] },
      { code: "x", actions: [] },
      { code: "tramites-traspasos", actions: [] },
      { code: "m", actions: [{ id: "r", slug: "matriculas.read" }, { id: "c", slug: "matriculas.create" }] },
    ];
    expect(sortServiceTypeModules(list).map((m) => m.code)).toEqual(["m", "tramites-traspasos", "tramites-otros", "x"]);
  });
});
