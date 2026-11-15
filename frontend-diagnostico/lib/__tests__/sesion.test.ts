import { describe, expect, it } from "vitest";
import { puedeVerReportes, tieneVistaGlobal } from "@/lib/sesion";

// Uso de ejemplo: puedeVerReportes(sesion.capacidades) decide la entrada «Reportes» del dock y la página /reportes;
// tieneVistaGlobal(sesion.capacidades) abre el Dashboard y los reportes en «Todas las compañías» (SP-2, Q4). Un back
// anterior a la 1.5.0 no manda los campos: ausente cuenta como false.

describe("capacidades nuevas de GET /sesion (contrato §0 SP-2)", () => {
  it("verReportes y verEstadisticasGlobales en true las habilitan", () => {
    expect(puedeVerReportes({ verReportes: true } as never)).toBe(true);
    expect(tieneVistaGlobal({ verEstadisticasGlobales: true } as never)).toBe(true);
  });

  it("la vista global también abre los reportes (rutas del backoffice)", () => {
    expect(puedeVerReportes({ verReportes: false, verEstadisticasGlobales: true } as never)).toBe(true);
  });

  it("ausentes o con sesión nula cuentan como false; backoffice solo no basta", () => {
    expect(puedeVerReportes({} as never)).toBe(false);
    expect(tieneVistaGlobal({ backoffice: true } as never)).toBe(false);
    expect(puedeVerReportes(null)).toBe(false);
    expect(tieneVistaGlobal(undefined)).toBe(false);
  });
});
