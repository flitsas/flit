// "Ayuda" es soporte universal: debe quedar siempre como módulo válido para que el dock
// la muestre y abra en cualquier pantalla (no debe rebotar a dashboard al navegar a ?m=ayuda).
import { describe, expect, it } from "vitest";
import { buildValidModules, parseModule } from "../modules";

describe("nav/modules — Ayuda universal", () => {
  it("incluye 'ayuda' aunque RBAC no la conceda", () => {
    const valid = buildValidModules(["dashboard", "tramites"]);
    expect(valid).toContain("ayuda");
  });

  it("no duplica 'ayuda' si RBAC ya la incluye", () => {
    const valid = buildValidModules(["dashboard", "ayuda"]);
    expect(valid.filter((m) => m === "ayuda")).toHaveLength(1);
  });

  it("navegar a ?m=ayuda no rebota a dashboard cuando RBAC no la incluye", () => {
    const valid = buildValidModules(["dashboard", "tramites"]);
    expect(parseModule("ayuda", valid)).toBe("ayuda");
  });

  it("un módulo desconocido sí cae a dashboard", () => {
    const valid = buildValidModules(["dashboard"]);
    expect(parseModule("inexistente", valid)).toBe("dashboard");
  });

  it("sin permisos cargados, devuelve todos los módulos conocidos", () => {
    expect(buildValidModules([])).toContain("rbac");
    expect(buildValidModules([])).toContain("ayuda");
  });
});
