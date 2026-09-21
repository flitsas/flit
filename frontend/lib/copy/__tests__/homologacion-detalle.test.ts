import { describe, expect, it } from "vitest";
import { OT_DETALLE_SECCIONES } from "@/components/admin/transit-offices/ClientProcedureDetailModal";
import { COPY, isNaCopyKey } from "../copy-catalog";

describe("HU #12696 homologación detalle y wizard", () => {
  it("AC1: secciones OT y ganadores A07–A10 coinciden con el glosario", () => {
    expect(OT_DETALLE_SECCIONES.find((s) => s.id === "actores")?.titulo).toBe(COPY.A07);
    expect(COPY.A07).toBe("Actores del trámite");
    expect(COPY.A08).toBe("Especificaciones técnicas");
    expect(COPY.A09Motor).toBe("Nº Motor");
    expect(COPY.A09Chasis).toBe("Nº Chasis");
    expect(COPY.A09Serie).toBe("Nº Serie");
    expect(COPY.A10).toBe("Capacidad");
    expect(COPY.A01).toBe("Vendedor");
  });

  it("AC2: A11 es N/A — OT no clona la pestaña FUR y expediente", () => {
    expect(isNaCopyKey("A11")).toBe(true);
    expect(OT_DETALLE_SECCIONES.map((s) => s.id)).toEqual(["vehiculo", "actores", "documentos"]);
    expect(OT_DETALLE_SECCIONES.some((s) => /FUR/i.test(s.titulo))).toBe(false);
  });

  it("AC3: Nº Motor / Chasis / Serie usan el mismo prefijo en OT y gestor", () => {
    const prefijos = [COPY.A09Motor, COPY.A09Chasis, COPY.A09Serie];
    expect(prefijos.every((l) => l.startsWith("Nº "))).toBe(true);
    expect(prefijos.some((l) => l.startsWith("N. "))).toBe(false);
  });
});
