// HU #10470 — helpers de navegación/formato del historial de improntas.
import { describe, expect, it } from "vitest";
import {
  formatImprontaHistorialDate,
  improntaDateFromToIso,
  improntaDateToToIso,
  improntasTabPath,
  IMPRONTAS_TABS,
} from "../improntas-nav";

describe("improntas-nav — HU #10470", () => {
  it("IMPRONTAS_TABS incluye formulario e historial", () => {
    expect(IMPRONTAS_TABS.map((t) => t.id)).toEqual(["formulario", "historial"]);
  });

  it("improntasTabPath arma las rutas del módulo", () => {
    expect(improntasTabPath("formulario")).toBe("/admin/improntas");
    expect(improntasTabPath("historial")).toBe("/admin/improntas/historial");
  });

  it("improntaDateFromToIso arma el límite inferior del día en UTC", () => {
    expect(improntaDateFromToIso("2026-06-01")).toBe("2026-06-01T00:00:00.000Z");
    expect(improntaDateFromToIso("")).toBeUndefined();
  });

  it("improntaDateToToIso arma el límite superior del día en UTC", () => {
    expect(improntaDateToToIso("2026-06-30")).toBe("2026-06-30T23:59:59.999Z");
    expect(improntaDateToToIso("")).toBeUndefined();
  });

  it("formatImprontaHistorialDate formatea una fecha ISO válida", () => {
    const formatted = formatImprontaHistorialDate("2026-06-30T15:04:00Z");
    expect(formatted).not.toBe("2026-06-30T15:04:00Z");
    expect(formatted.length).toBeGreaterThan(0);
  });

  it("formatImprontaHistorialDate devuelve el valor original si no es una fecha válida", () => {
    expect(formatImprontaHistorialDate("no-es-fecha")).toBe("no-es-fecha");
  });
});
