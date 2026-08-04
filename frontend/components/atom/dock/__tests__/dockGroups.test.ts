import { describe, expect, it } from "vitest";
import { buildDockGroups, type DockEntryLike } from "../dockGroups";
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
});
