// HU12850 (Feature #12846, Épica #12751) — Preasignación: retirar consola y navegación.
//
// Uso de ejemplo: este spec no importa componentes React; verifica contrato de datos (ot-nav,
// dockGroups) y la ausencia física de los archivos retirados (AC2), que es lo único que puede
// demostrar de forma unitaria que la ruta ya no renderiza nada (Next.js resolvería 404 en runtime).
import { describe, expect, it } from "vitest";
import { existsSync } from "node:fs";
import path from "node:path";
import {
  DOCK_GROUP_ICON,
  DOCK_GROUP_LABEL,
  DOCK_GROUP_ORDER,
  DOCK_GROUP_SIDE,
  DOCK_ITEM_GROUP,
} from "@/components/atom/dock/dockGroups";
import { OT_ADM_DOCK, OT_HUB_TABS } from "../ot-nav";

const FRONTEND_ROOT = path.resolve(__dirname, "../../../..");

describe("HU12850 — Preasignación: retirar consola y navegación", () => {
  it("AC1 — el grupo 'preasignacion' desaparece de DOCK_GROUP_ORDER", () => {
    expect(DOCK_GROUP_ORDER).not.toContain("preasignacion");
  });

  it("AC1 — OT_HUB_TABS ya no incluye la pestaña 'plate-ranges'", () => {
    expect(OT_HUB_TABS.some((t) => (t.id as string) === "plate-ranges")).toBe(false);
  });

  it("AC2 — la página de la consola y el componente PlateRangesConsole ya no existen en el repo", () => {
    expect(
      existsSync(path.join(FRONTEND_ROOT, "app/admin/transit-offices/[id]/plate-ranges/page.tsx")),
    ).toBe(false);
    expect(
      existsSync(
        path.join(FRONTEND_ROOT, "components/admin/transit-offices/PlateRangesConsole.tsx"),
      ),
    ).toBe(false);
  });

  it("AC3 — DOCK_GROUP_ORDER, DOCK_GROUP_SIDE, DOCK_GROUP_LABEL, DOCK_GROUP_ICON y DOCK_ITEM_GROUP quedan consistentes (misma ausencia en los cinco mapas)", () => {
    expect(DOCK_GROUP_ORDER).not.toContain("preasignacion");
    expect(Object.keys(DOCK_GROUP_SIDE)).not.toContain("preasignacion");
    expect(Object.keys(DOCK_GROUP_LABEL)).not.toContain("preasignacion");
    expect(Object.keys(DOCK_GROUP_ICON)).not.toContain("preasignacion");
    expect(Object.values(DOCK_ITEM_GROUP)).not.toContain("preasignacion");
    // Registro doble (memoria del proyecto): ni la key del dock Admin OT sobrevive.
    expect(Object.keys(OT_ADM_DOCK)).not.toContain("preasignacion");
  });

  it("AC3 — lib/api/admin-plate-ranges.ts solo conserva assign/release/update de la placa del trámite", async () => {
    const mod = await import("@/lib/api/admin-plate-ranges");
    expect(typeof mod.assignPlateToProcedure).toBe("function");
    expect(typeof mod.releaseProcedurePlate).toBe("function");
    expect(typeof mod.updateProcedurePlate).toBe("function");
    expect((mod as Record<string, unknown>).listPlateRanges).toBeUndefined();
    expect((mod as Record<string, unknown>).listPlateDetails).toBeUndefined();
    expect((mod as Record<string, unknown>).listEligibleCompanies).toBeUndefined();
    expect((mod as Record<string, unknown>).assignPlateRange).toBeUndefined();
    expect((mod as Record<string, unknown>).editPlateRange).toBeUndefined();
    expect((mod as Record<string, unknown>).blockPlate).toBeUndefined();
    expect((mod as Record<string, unknown>).unblockPlate).toBeUndefined();
    expect((mod as Record<string, unknown>).revokePlate).toBeUndefined();
  });
});
