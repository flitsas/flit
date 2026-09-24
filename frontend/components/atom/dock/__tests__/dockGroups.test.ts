import { describe, expect, it } from "vitest";
import {
  buildDockGroups,
  DOCK_GROUP_ICON,
  DOCK_GROUP_LABEL,
  DOCK_GROUP_ORDER,
  DOCK_GROUP_SIDE,
  DOCK_ITEM_GROUP,
  flattenDockEntries,
  type DockEntryLike,
} from "../dockGroups";
import { OT_ADM_DOCK } from "@/components/admin/transit-offices/ot-nav";
import { LayoutGrid } from "lucide-react";

function entry(key: string, label: string, children?: DockEntryLike[]): DockEntryLike {
  return {
    key,
    label,
    icon: LayoutGrid,
    active: false,
    onClick: () => undefined,
    children,
  };
}

describe("buildDockGroups", () => {
  it("expone Trámites e Identidad como píldoras planas (sin submenú Operación)", () => {
    const groups = buildDockGroups([
      entry("tramites", "Trámites"),
      entry("validaciones", "Identidad"),
      entry("reportes", "Reportes"),
    ]);
    expect(groups.map((g) => g.label)).toEqual(["Trámites", "Identidad", "Reportes"]);
    expect(groups[0].items).toHaveLength(1);
    expect(groups[1].items).toHaveLength(1);
    expect(groups[0].items[0].label).toBe("Trámites");
    expect(groups[1].items[0].label).toBe("Identidad");
  });

  it("no agrupa dashboard (retirado del dock; FAB = Inicio)", () => {
    const groups = buildDockGroups([
      entry("dashboard", "Dashboard"),
      entry("tramites", "Trámites"),
    ]);
    expect(groups.map((g) => g.label)).toEqual(["Trámites"]);
  });

  // HU #12850 (Feature #12846) — el grupo `preasignacion` se retiró del dock junto con la consola.
  it("Admin OT: Administración agrupa Reglas/Documentos/Requisitos (sin Preasignación)", () => {
    const groups = buildDockGroups([
      entry(OT_ADM_DOCK.tramites, "Trámites"),
      entry(OT_ADM_DOCK.rules, "Reglas"),
      entry(OT_ADM_DOCK.documents, "Documentos"),
      entry(OT_ADM_DOCK.requirements, "Requisitos"),
      entry(OT_ADM_DOCK.usuarios, "Usuarios"),
      entry(OT_ADM_DOCK.reportes, "Reportes"),
      entry(OT_ADM_DOCK.mandatos, "Mandatos"),
      entry(OT_ADM_DOCK.imprintValidation, "Validar impronta"),
    ]);
    expect(groups.map((g) => g.label)).toEqual([
      "Trámites",
      "Reportes",
      "Usuarios",
      "Administración",
    ]);
    const admin = groups.find((g) => g.id === "administracion");
    expect(admin?.items.map((i) => i.label)).toEqual([
      "Reglas",
      "Documentos",
      "Requisitos",
      "Mandatos",
      "Validar impronta",
    ]);
  });

  // HU12850 AC3 — DOCK_GROUP_ORDER, DOCK_GROUP_SIDE, DOCK_GROUP_LABEL, DOCK_GROUP_ICON y
  // DOCK_ITEM_GROUP quedan consistentes: `preasignacion` no aparece en ninguno de los cinco mapas
  // (registro doble: memoria del proyecto — un mapa a medias descarta el ítem en silencio).
  it("HU12850 AC3 — ningún mapa del dock conserva la clave 'preasignacion'", () => {
    expect(DOCK_GROUP_ORDER).not.toContain("preasignacion");
    expect(Object.keys(DOCK_GROUP_SIDE)).not.toContain("preasignacion");
    expect(Object.keys(DOCK_GROUP_LABEL)).not.toContain("preasignacion");
    expect(Object.keys(DOCK_GROUP_ICON)).not.toContain("preasignacion");
    expect(Object.values(DOCK_ITEM_GROUP)).not.toContain("preasignacion");
    expect(Object.keys(OT_ADM_DOCK)).not.toContain("preasignacion");
  });

  it("SuperAdmin: Plataforma (con Mandatos y Notificaciones) vive anidada en Administradores", () => {
    const groups = buildDockGroups([
      entry("admin-companies", "Compañías"),
      entry("admin-transit", "Tránsito"),
      entry("admin-documents", "Documental"),
      entry("admin-improntas", "Improntas"),
      entry("admin-quipux", "Quipux"),
      entry("admin-jobs", "Procesos periódicos"),
      entry("rbac", "RBAC Admin"),
      entry("auditoria", "Auditoría"),
      entry("admin-plataforma", "Plataforma", [
        entry("admin-mandatos", "Mandatos"),
        entry("admin-fur", "FUR"),
        entry("admin-notificaciones", "Notificaciones"),
      ]),
    ]);
    expect(groups).toHaveLength(1);
    expect(groups[0].label).toBe("Administradores");
    expect(groups[0].items.map((i) => i.label)).toEqual([
      "Compañías",
      "Tránsito",
      "Documental",
      "Improntas",
      "Quipux",
      "Procesos periódicos",
      "RBAC Admin",
      "Auditoría",
      "Plataforma",
    ]);
    const plataforma = groups[0].items.find((i) => i.key === "admin-plataforma");
    expect(plataforma?.children?.map((c) => c.label)).toEqual(["Mandatos", "FUR", "Notificaciones"]);
  });

  it("flattenDockEntries expone Mandatos, FUR y Notificaciones para la hoja móvil", () => {
    const flat = flattenDockEntries([
      entry("admin-companies", "Compañías"),
      entry("admin-plataforma", "Plataforma", [
        entry("admin-mandatos", "Mandatos"),
        entry("admin-fur", "FUR"),
        entry("admin-notificaciones", "Notificaciones"),
      ]),
    ]);
    expect(flat.map((i) => i.label)).toEqual(["Compañías", "Mandatos", "FUR", "Notificaciones"]);
  });

  it("HU #12723 — cada agrupador declara lado izquierdo o derecho del FAB", () => {
    expect(DOCK_GROUP_SIDE.tramites).toBe("left");
    expect(DOCK_GROUP_SIDE.identidad).toBe("left");
    expect(DOCK_GROUP_SIDE.reportes).toBe("left");
    expect(DOCK_GROUP_SIDE.usuarios).toBe("right");
    expect(DOCK_GROUP_SIDE.administracion).toBe("right");
    expect(DOCK_GROUP_SIDE.administradores).toBe("right");
    expect(DOCK_GROUP_SIDE.integraciones).toBe("right");
  });

  it("HU #12723 — Ayuda no está en DOCK_GROUP_ORDER (sale del dock)", () => {
    expect(DOCK_GROUP_ORDER).not.toContain("ayuda");
    expect(Object.keys(DOCK_GROUP_SIDE)).not.toContain("ayuda");
  });

  it("Integraciones agrupa Log QX e ICT, con Log ICT y Reportes ICT anidados bajo ICT", () => {
    const groups = buildDockGroups([
      entry("log-qx", "Log QX"),
      entry("ict", "ICT", [entry("ict-logs", "Log ICT"), entry("ict-reportes", "Reportes ICT")]),
    ]);
    expect(groups).toHaveLength(1);
    expect(groups[0].label).toBe("Integraciones");
    expect(groups[0].items.map((i) => i.label)).toEqual(["Log QX", "ICT"]);
    const ict = groups[0].items.find((i) => i.key === "ict");
    expect(ict?.children?.map((c) => c.label)).toEqual(["Log ICT", "Reportes ICT"]);
  });
});
