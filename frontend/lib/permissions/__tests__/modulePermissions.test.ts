import { describe, expect, it } from "vitest";
import { buildModuleViews, outOfCapOverrideIds } from "../modulePermissions";
import type { AccessibleModule } from "@/lib/api/security";
import type { GrantablePermissionItem, UserPermissionOverride } from "@/lib/api/permission-overrides";

const modules: AccessibleModule[] = [
  {
    id: "m1", code: "tramites", name: "Trámites", sortOrder: 1,
    actions: [
      { id: "p1", slug: "tramites.view", name: "Ver" },
      { id: "p2", slug: "tramites.create", name: "Crear" },
      { id: "p3", slug: "tramites.legacy", name: "Heredada no exigida" },
    ],
  },
  { id: "m2", code: "reportes", name: "Reportes", sortOrder: 2, actions: [{ id: "p4", slug: "reportes.view", name: "Ver" }] },
  { id: "m3", code: "vacio", name: "Sin exigidos", sortOrder: 3, actions: [{ id: "p5", slug: "vacio.x", name: "X" }] },
];

const grantable: GrantablePermissionItem[] = ["p1", "p2", "p4"].map((id) => ({
  id, slug: id, name: id, moduleCode: "x", productCode: "tramites",
}));

const ov = (permissionId: string, effect: "allow" | "deny"): UserPermissionOverride => ({
  permissionId, slug: permissionId, productCode: "tramites", effect, source: "INDIVIDUAL",
  grantedBy: "u", grantedByName: "Ana Admin", grantedAt: "2026-10-19T19:05:00Z",
});

describe("buildModuleViews", () => {
  it("AC1: sin overrides todo es heredado y el estado sale del rol", () => {
    const views = buildModuleViews(modules, grantable, new Set(["p1"]), []);
    const m1 = views[0];
    expect(m1.origin).toBe("inherited");
    expect(m1.actions.map((a) => [a.id, a.origin, a.enabled])).toEqual([
      ["p1", "inherited", true],
      ["p2", "inherited", false],
    ]);
    expect(m1.enabledCount).toBe(1);
  });

  it("AC1: acciones mixtas dejan el módulo en Parcial; allow habilita y deny deshabilita", () => {
    const views = buildModuleViews(modules, grantable, new Set(["p1"]), [ov("p2", "allow")]);
    expect(views[0].origin).toBe("partial");
    const p2 = views[0].actions.find((a) => a.id === "p2")!;
    expect(p2).toMatchObject({ origin: "individual", effect: "allow", enabled: true, grantedByName: "Ana Admin" });

    const denied = buildModuleViews(modules, grantable, new Set(["p1"]), [ov("p1", "deny")]);
    expect(denied[0].actions[0]).toMatchObject({ origin: "individual", effect: "deny", enabled: false });
  });

  it("AC1: todas las acciones con permiso individual → módulo Individual", () => {
    const views = buildModuleViews(modules, grantable, new Set(), [ov("p1", "allow"), ov("p2", "deny")]);
    expect(views[0].origin).toBe("individual");
  });

  it("AC2: lo que no es otorgable (enforced distinto de true) no aparece, ni siquiera heredado", () => {
    const views = buildModuleViews(modules, grantable, new Set(["p3", "p5"]), []);
    expect(views.flatMap((m) => m.actions.map((a) => a.id))).not.toContain("p3");
    expect(views.map((m) => m.code)).toEqual(["tramites", "reportes"]);
  });

  it("AC6: conserva el orden y los nombres del catálogo de módulos", () => {
    const views = buildModuleViews(modules, grantable, new Set(), []);
    expect(views.map((m) => m.name)).toEqual(["Trámites", "Reportes"]);
    expect(views[0].actions.map((a) => a.name)).toEqual(["Ver", "Crear"]);
  });
});

describe("buildModuleViews — tope del administrador (#13655)", () => {
  const cap: GrantablePermissionItem[] = [{ id: "p1", slug: "p1", name: "p1", moduleCode: "x", productCode: "tramites" }];

  it("un permiso individual fuera del tope se marca outOfCap; los dentro del tope no", () => {
    const views = buildModuleViews(modules, cap, new Set(), [ov("p2", "allow"), ov("p1", "allow")]);
    const byId = Object.fromEntries(views[0].actions.map((a) => [a.id, a]));
    expect(byId.p2.outOfCap).toBe(true);
    expect(byId.p1.outOfCap).toBeUndefined();
  });

  it("un id bloqueado sigue visible, sin override, como outOfCap; sin bloquear desaparece", () => {
    const sinBloqueo = buildModuleViews(modules, cap, new Set(), []);
    expect(sinBloqueo[0].actions.map((a) => a.id)).toEqual(["p1"]);
    const conBloqueo = buildModuleViews(modules, cap, new Set(), [], new Set(["p2"]));
    const p2 = conBloqueo[0].actions.find((a) => a.id === "p2");
    expect(p2).toMatchObject({ origin: "inherited", enabled: false, outOfCap: true });
  });

  it("outOfCapOverrideIds devuelve solo los overrides fuera del tope", () => {
    expect(outOfCapOverrideIds(cap, [ov("p1", "allow"), ov("p2", "deny")])).toEqual(["p2"]);
  });
});
