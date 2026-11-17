import { describe, expect, it } from "vitest";
import type { CapacidadesDiagnostico } from "@/lib/sesion";
import { puedeGestionarSaneamiento, puedeSanear } from "@/lib/sesion";

// Uso de ejemplo: puedeSanear(sesion.capacidades) muestra la bandera; puedeGestionarSaneamiento(…) la bandeja del backoffice.
// Plan §5.3: las dos capacidades son opcionales en el tipo; un backend anterior no las manda y cuentan como false.

const base: CapacidadesDiagnostico = {
  crearDiagnosticos: true,
  verDiagnosticos: true,
  descargarReportes: true,
  derivar: false,
  verConfiguracion: false,
  editarConfiguracion: false,
  reprocesarConsultas: false,
  backoffice: false,
  editarContrato: false,
  llenarConsultas: false,
  corregirDictamen: false,
  verAuditoria: false,
  verVolumetria: false,
};

describe("capacidades de saneamiento en la sesión (plan §5.3)", () => {
  it("ausentes (backend anterior) cuentan como false", () => {
    expect(puedeSanear(base)).toBe(false);
    expect(puedeGestionarSaneamiento(base)).toBe(false);
    expect(puedeSanear(null)).toBe(false);
    expect(puedeGestionarSaneamiento(undefined)).toBe(false);
  });

  it("solo true habilita", () => {
    expect(puedeSanear({ ...base, sanear: true })).toBe(true);
    expect(puedeGestionarSaneamiento({ ...base, gestionarSaneamiento: true })).toBe(true);
    expect(puedeSanear({ ...base, sanear: false })).toBe(false);
  });
});
