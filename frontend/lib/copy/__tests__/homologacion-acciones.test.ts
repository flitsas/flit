import { describe, expect, it } from "vitest";
import { COPY } from "../copy-catalog";

const COPY_VALUES = new Set<string>(Object.values(COPY));

describe("HU #12697 homologación acciones compartidas", () => {
  it("AC1: Ver consolidado y Exportar usan el ganador en carga y en reposo", () => {
    expect(COPY.A12).toBe("Ver consolidado");
    expect(COPY.A13).toBe("Exportar");
    expect(COPY.A12).not.toBe("Ver consolidado del expediente");
    expect(COPY.A13).not.toBe("Exportar a Excel");
    expect(COPY.A12).not.toBe("Abriendo…");
  });

  it("AC2: acciones solo-OT o solo-gestor no entran al catálogo compartido", () => {
    expect(COPY_VALUES.has("Asignar placa")).toBe(false);
    expect(COPY_VALUES.has("Enviar al OT")).toBe(false);
    expect(COPY_VALUES.has("Liberar placa")).toBe(false);
  });

  it("AC3: Aprobar / Rechazar / Adjuntar LT son un solo largo canónico", () => {
    expect(COPY.E01Aprobar).toBe("Aprobar");
    expect(COPY.E01Rechazar).toBe("Rechazar");
    expect(COPY.E02).toBe("Adjuntar LT");
    expect(COPY.E01Aprobar).not.toBe("Aprobar trámite");
    expect(COPY.E01Rechazar).not.toBe("Rechazar trámite");
  });
});
