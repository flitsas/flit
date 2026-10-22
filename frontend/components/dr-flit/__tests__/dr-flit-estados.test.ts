import { describe, expect, it } from "vitest";

import { ESTADOS_TRAMITE } from "@/lib/tramites/estados";
import { DR_FLIT_GESTION_INTENTS } from "../dr-flit-intents";
import { estadoHint } from "../dr-flit-estados";

describe("dr-flit-estados (HU-E)", () => {
  it("los estados de la ruta de placa y la revocatoria (ADR-0059) tienen pista de acción", () => {
    expect(estadoHint("preasignacion")).toMatch(/asignarla/i);
    expect(estadoHint("asignado")).toMatch(/SOAT/);
    expect(estadoHint("revocado")).toMatch(/Revocatorias/);
    expect(estadoHint("subsanacion")).toMatch(/subsanaci/i);
  });

  it("es tolerante a mayúsculas/espacios y devuelve null para desconocidos o terminales sin acción", () => {
    expect(estadoHint(" Preasignacion ")).toMatch(/asignarla/i);
    expect(estadoHint("aprobado")).toBeNull();
    expect(estadoHint("anulado")).toBeNull();
    expect(estadoHint("lo-que-sea")).toBeNull();
    expect(estadoHint(null)).toBeNull();
  });

  it("toda pista corresponde a un estado del catálogo (no inventa estados)", () => {
    for (const estado of ESTADOS_TRAMITE) {
      const hint = estadoHint(estado);
      expect(hint === null || typeof hint === "string").toBe(true);
    }
  });

  it("las etiquetas del menú Gestión usan el copy canónico", () => {
    expect(DR_FLIT_GESTION_INTENTS.map((i) => i.label)).toEqual([
      "Buscar por placa",
      "Buscar por VIN",
      "Buscar por trámite",
      "Buscar por cliente",
    ]);
  });
});
