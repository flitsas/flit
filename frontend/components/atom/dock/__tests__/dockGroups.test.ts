import { describe, expect, it } from "vitest";
import { buildDockGroups, type DockEntryLike } from "../dockGroups";
import { OT_ADM_DOCK } from "@/components/admin/transit-offices/ot-nav";
import { LayoutGrid } from "lucide-react";

function entry(key: string, label: string): DockEntryLike {
  return { key, label, icon: LayoutGrid, active: false, onClick: () => undefined };
}

describe("buildDockGroups", () => {
  it("expone Trámites e Identidad como píldoras planas (sin submenú Operación)", () => {
    const groups = buildDockGroups([
      entry("tramites", "Trámites"),
      entry("validaciones", "Identidad"),
      entry("reportes", "Reportes"),
      entry("ayuda", "Ayuda"),
    ]);
    expect(groups.map((g) => g.label)).toEqual(["Trámites", "Identidad", "Reportes", "Ayuda"]);
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

  it("Admin OT: Administración agrupa Reglas/Documentos/Requisitos; Preasignación es píldora", () => {
    const groups = buildDockGroups([
      entry(OT_ADM_DOCK.tramites, "Trámites"),
      entry(OT_ADM_DOCK.rules, "Reglas"),
      entry(OT_ADM_DOCK.documents, "Documentos"),
      entry(OT_ADM_DOCK.requirements, "Requisitos"),
      entry(OT_ADM_DOCK.preasignacion, "Preasignación"),
      entry(OT_ADM_DOCK.usuarios, "Usuarios"),
      entry(OT_ADM_DOCK.reportes, "Reportes"),
    ]);
    expect(groups.map((g) => g.label)).toEqual([
      "Trámites",
      "Preasignación",
      "Reportes",
      "Usuarios",
      "Administración",
    ]);
    const admin = groups.find((g) => g.id === "administracion");
    expect(admin?.items.map((i) => i.label)).toEqual(["Reglas", "Documentos", "Requisitos"]);
  });

  it("SuperAdmin: Tránsito es píldora única del agrupador OT", () => {
    const groups = buildDockGroups([entry("admin-transit", "Tránsito")]);
    expect(groups).toHaveLength(1);
    expect(groups[0].label).toBe("OT");
    expect(groups[0].items).toHaveLength(1);
    expect(groups[0].items[0].label).toBe("Tránsito");
  });
});
