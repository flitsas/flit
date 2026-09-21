import { describe, expect, it } from "vitest";
import { isNaCopyKey } from "../copy-catalog";
import {
  ESTADOS_TRAMITE,
  estadoLabel,
  estadoLabelConOrigen,
  RECHAZADO_PREASIGNACION_LABEL,
} from "@/lib/tramites/estados";
import { formatOtProcedureStatus } from "@/components/admin/transit-offices/ot-utils";
import { OT_BANDEJA_TARJETAS } from "@/components/admin/transit-offices/OtBandejaCounters";

describe("HU #12698 homologación KPI y chips (A14–A16 N/A)", () => {
  it("AC1: A14–A16 son N/A; KPI OT conserva cola y chip sigue el catálogo B", () => {
    expect(isNaCopyKey("A14")).toBe(true);
    expect(isNaCopyKey("A15")).toBe(true);
    expect(isNaCopyKey("A16")).toBe(true);

    expect(OT_BANDEJA_TARJETAS.find((t) => t.key === "porDecidir")?.label).toBe("Por decidir");
    expect(OT_BANDEJA_TARJETAS.find((t) => t.key === "asignados")?.label).toBe("Asignados");
    expect(OT_BANDEJA_TARJETAS.find((t) => t.key === "porDecidir")?.status).toBe("entregado");

    expect(estadoLabel("entregado")).toBe("Entregado");
    expect(estadoLabel("asignado")).toBe("Asignado");
  });

  it("AC2: el OT no inventa un quinto estado; el distintivo de preasignación queda en el gestor", () => {
    expect(ESTADOS_TRAMITE).not.toContain("rechazado_preasignacion");
    expect(formatOtProcedureStatus("rechazado")).toBe("Rechazado");
    expect(formatOtProcedureStatus("rechazado")).not.toBe(RECHAZADO_PREASIGNACION_LABEL);
    expect(estadoLabelConOrigen("rechazado", "preasignacion")).toBe(RECHAZADO_PREASIGNACION_LABEL);
  });

  it("AC3: el chip de entregado dice Entregado aunque la tarjeta KPI se llame Por decidir", () => {
    expect(formatOtProcedureStatus("entregado")).toBe("Entregado");
    expect(formatOtProcedureStatus("entregado")).toBe(estadoLabel("entregado"));
    expect(OT_BANDEJA_TARJETAS.find((t) => t.status === "entregado")?.label).not.toBe(
      formatOtProcedureStatus("entregado"),
    );
  });
});
