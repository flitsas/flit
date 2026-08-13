// Invariante dock ≡ URL: resolveNavigableModuleIds es la fuente única (deny-by-default).
import { describe, expect, it } from "vitest";
import {
  ALL_MODULE_IDS,
  buildValidModules,
  parseModule,
  resolveNavigableModuleIds,
  UNIVERSAL_MODULE_IDS,
} from "../modules";

describe("nav/modules — resolveNavigableModuleIds", () => {
  const base = {
    accessibleCodes: [] as string[],
    isSuperAdmin: false,
    isOtAdmin: false,
    canReadLogQx: false,
    canReadIctLogs: false,
  };

  it("incluye 'ayuda' aunque RBAC no la conceda", () => {
    const valid = resolveNavigableModuleIds({
      ...base,
      accessibleCodes: ["dashboard", "tramites"],
    });
    expect(valid).toContain("ayuda");
    expect(valid).toContain("tramites");
    expect(valid).toContain("dashboard");
  });

  it("no duplica 'ayuda' si RBAC ya la incluye", () => {
    const valid = resolveNavigableModuleIds({
      ...base,
      accessibleCodes: ["dashboard", "ayuda"],
    });
    expect(valid.filter((m) => m === "ayuda")).toHaveLength(1);
  });

  it("navegar a ?m=ayuda no rebota a dashboard cuando RBAC no la incluye", () => {
    const valid = resolveNavigableModuleIds({
      ...base,
      accessibleCodes: ["dashboard", "tramites"],
    });
    expect(parseModule("ayuda", valid)).toBe("ayuda");
  });

  it("un módulo desconocido sí cae a dashboard", () => {
    const valid = resolveNavigableModuleIds({
      ...base,
      accessibleCodes: ["dashboard"],
    });
    expect(parseModule("inexistente", valid)).toBe("dashboard");
  });

  it("sin permisos cargados, deny-by-default (no ALL_MODULE_IDS)", () => {
    const valid = resolveNavigableModuleIds(base);
    expect(valid).toEqual(expect.arrayContaining(["dashboard", "ayuda"]));
    expect(valid).not.toContain("rbac");
    expect(valid).not.toContain("validaciones");
    expect(valid).not.toContain("auditoria");
    expect(valid).not.toContain("log-qx");
    expect(valid).not.toContain("ict-logs");
    // No bypass histórico accessibleCodes=[] → catálogo completo
    expect(valid.length).toBeLessThan(ALL_MODULE_IDS.length);
  });

  it("solo 'ayuda' es universal de navegación", () => {
    expect(UNIVERSAL_MODULE_IDS).toEqual(["ayuda"]);
  });

  it("auditoria / log-qx / ict-logs solo con claims del dock", () => {
    expect(resolveNavigableModuleIds(base)).not.toContain("auditoria");
    expect(
      resolveNavigableModuleIds({ ...base, isSuperAdmin: true }),
    ).toEqual(expect.arrayContaining(["rbac", "auditoria", "dashboard", "ayuda"]));
    expect(
      resolveNavigableModuleIds({ ...base, canReadLogQx: true }),
    ).toContain("log-qx");
    expect(
      resolveNavigableModuleIds({ ...base, canReadIctLogs: true }),
    ).toContain("ict-logs");
  });

  it("OT admin omite SPA homónimas del hub (otAdminSpaOmit)", () => {
    const valid = resolveNavigableModuleIds({
      ...base,
      isOtAdmin: true,
      accessibleCodes: ["tramites", "reportes", "reportes-detallados", "usuarios", "validaciones"],
    });
    expect(valid).not.toContain("tramites");
    expect(valid).not.toContain("reportes");
    expect(valid).not.toContain("reportes-detallados");
    expect(valid).not.toContain("usuarios");
    expect(valid).toContain("validaciones");
    expect(valid).toContain("ayuda");
  });

  it("no incluye rutas /admin en el catálogo ?m=", () => {
    const valid = resolveNavigableModuleIds({ ...base, isSuperAdmin: true });
    expect(valid.every((id) => !id.startsWith("admin"))).toBe(true);
  });

  it("parseModule con lista vacía no autoriza (deny-by-default hold)", () => {
    expect(parseModule("validaciones", [])).toBe("dashboard");
    expect(parseModule("rbac", [])).toBe("dashboard");
  });

  it("buildValidModules (compat) también es deny-by-default sin claims", () => {
    expect(buildValidModules([])).toContain("ayuda");
    expect(buildValidModules([])).toContain("dashboard");
    expect(buildValidModules([])).not.toContain("rbac");
  });
});
