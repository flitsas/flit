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
  // Epic #12686 (decisión 6) — las tarjetas del OT pasan a llamarse como el estado, igual que en el
  // listado del gestor: «Por decidir» → «Entregado», «Asignados» → «Asignado». La clave no cambia.
  it("AC1: A14–A16 son N/A; la tarjeta KPI del OT lleva el nombre del estado", () => {
    expect(isNaCopyKey("A14")).toBe(true);
    expect(isNaCopyKey("A15")).toBe(true);
    expect(isNaCopyKey("A16")).toBe(true);

    expect(OT_BANDEJA_TARJETAS.find((t) => t.key === "porDecidir")?.label).toBe("Entregado");
    expect(OT_BANDEJA_TARJETAS.find((t) => t.key === "asignados")?.label).toBe("Asignado");
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

  it("AC3: el chip de entregado y la tarjeta KPI dicen lo mismo (Epic #12686)", () => {
    expect(formatOtProcedureStatus("entregado")).toBe("Entregado");
    expect(formatOtProcedureStatus("entregado")).toBe(estadoLabel("entregado"));
    expect(OT_BANDEJA_TARJETAS.find((t) => t.status === "entregado")?.label).toBe(
      formatOtProcedureStatus("entregado"),
    );
  });
});
