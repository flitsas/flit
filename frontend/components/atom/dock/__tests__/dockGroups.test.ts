import { describe, expect, it } from "vitest";
import { buildDockGroups, type DockEntryLike } from "../dockGroups";
import { LayoutGrid } from "lucide-react";

function entry(key: string, label: string): DockEntryLike {
  return { key, label, icon: LayoutGrid, active: false, onClick: () => undefined };
}

describe("buildDockGroups", () => {
  it("agrupa según el mapa de menú/submenú y omite vacíos", () => {
    const groups = buildDockGroups([
      entry("dashboard", "Dashboard"),
      entry("tramites", "Trámites"),
      entry("validaciones", "Validaciones"),
      entry("reportes", "Reportes"),
      entry("ayuda", "Ayuda"),
    ]);
    expect(groups.map((g) => g.label)).toEqual(["Operación", "Reportes", "Ayuda"]);
    expect(groups[0].items.map((i) => i.label)).toEqual(["Dashboard", "Trámites", "Validaciones"]);
  });
});
