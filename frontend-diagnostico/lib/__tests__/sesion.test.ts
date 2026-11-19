import { describe, expect, it } from "vitest";
import { puedeGestionarUsuarios, puedeVerReportes, tieneVistaGlobal } from "@/lib/sesion";

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

// HU #14588 (contrato §3.1, SP-17): `gestionarUsuarios` abre la entrada «Usuarios» y la página /usuarios. Un back
// anterior no la manda: ausente cuenta como false. La API vuelve a exigir el permiso en cada ruta.
describe("HU14588 capacidad gestionarUsuarios", () => {
  it("en true la habilita", () => {
    expect(puedeGestionarUsuarios({ gestionarUsuarios: true } as never)).toBe(true);
  });

  it("ausente, false, backoffice solo o sesión nula cuentan como false", () => {
    expect(puedeGestionarUsuarios({} as never)).toBe(false);
    expect(puedeGestionarUsuarios({ gestionarUsuarios: false } as never)).toBe(false);
    expect(puedeGestionarUsuarios({ backoffice: true } as never)).toBe(false);
    expect(puedeGestionarUsuarios(null)).toBe(false);
    expect(puedeGestionarUsuarios(undefined)).toBe(false);
  });
});
