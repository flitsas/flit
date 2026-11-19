// HU #14074 — reglas puras del cliente de tarifas: formato de pesos escrito a mano, lectura de lo que escribe la
// persona (negativos, letras, decimales, tope), fecha de hoy en Colombia y agrupación por ámbito.
import { describe, expect, it } from "vitest";
import {
  VALOR_MAXIMO_COP,
  agruparTarifas,
  caretTrasDigitos,
  formatPesos,
  hoyColombiaIso,
  parsePesos,
  textoPesos,
  type TarifaPn,
} from "../admin-pn-tarifas";

const tarifa = (over: Partial<TarifaPn>): TarifaPn => ({
  id: "t",
  concepto: "DERECHOS_TRANSITO",
  tipoTramiteId: "tipo-1",
  tipoTramiteCodigo: "TRASPASO",
  tipoTramiteNombre: "Traspaso",
  organismoId: null,
  organismoCodigo: null,
  organismoNombre: null,
  valorCop: 100000,
  vigenteDesde: "2026-01-01",
  vigenteHasta: null,
  estado: "vigente",
  creadaEn: "2026-01-01T00:00:00Z",
  creadaPor: null,
  actualizadaEn: null,
  actualizadaPor: null,
  ...over,
});

describe("formatPesos", () => {
  it("usa punto de miles sin depender del locale", () => {
    expect(formatPesos(0)).toBe("$ 0");
    expect(formatPesos(950)).toBe("$ 950");
    expect(formatPesos(1250000)).toBe("$ 1.250.000");
    expect(formatPesos(1_000_000_000)).toBe("$ 1.000.000.000");
  });
  it("sin valor muestra una raya", () => {
    expect(formatPesos(null)).toBe("—");
  });
});

describe("textoPesos", () => {
  it("rearma el punto de miles al escribir y al borrar un dígito", () => {
    expect(textoPesos("1250")).toBe("1.250");
    expect(textoPesos("1.250.000")).toBe("1.250.000");
    expect(textoPesos("1.250.00")).toBe("125.000");
    expect(textoPesos("1.25.000")).toBe("125.000");
    expect(textoPesos("1,250,000")).toBe("1.250.000");
    expect(textoPesos("-5")).toBe("-5");
    expect(textoPesos("abc")).toBe("abc");
    expect(textoPesos("12,5")).toBe("12,5");
    expect(textoPesos("")).toBe("");
    expect(textoPesos("0")).toBe("0");
    expect(caretTrasDigitos("125.000", 6)).toBe(7);
    expect(caretTrasDigitos("125.000", 3)).toBe(3);
  });
});

describe("parsePesos", () => {
  it("acepta enteros con o sin puntos de miles y símbolo", () => {
    expect(parsePesos("1250000")).toEqual({ ok: true, valor: 1250000 });
    expect(parsePesos("1.250.000")).toEqual({ ok: true, valor: 1250000 });
    expect(parsePesos("$ 85.000")).toEqual({ ok: true, valor: 85000 });
    expect(parsePesos("0")).toEqual({ ok: true, valor: 0 });
  });
  it("rechaza negativos, letras, decimales y separadores mal puestos con un mensaje claro", () => {
    expect(parsePesos("-100")).toEqual({ ok: false, mensaje: "El valor no puede ser negativo." });
    for (const malo of ["abc", "12,5", "12.5", "1.2.3", "1 2e3"]) {
      const r = parsePesos(malo);
      expect(r.ok).toBe(false);
      if (!r.ok) expect(r.mensaje).toMatch(/solo números enteros/i);
    }
    expect(parsePesos("   ")).toEqual({ ok: false, mensaje: "Escriba el valor en pesos." });
  });
  it("rechaza lo que supere el tope del backend", () => {
    expect(parsePesos(String(VALOR_MAXIMO_COP)).ok).toBe(true);
    const r = parsePesos(String(VALOR_MAXIMO_COP + 1));
    expect(r.ok).toBe(false);
  });
});

describe("hoyColombiaIso", () => {
  it("resta las cinco horas de Colombia: a las 02:00 UTC todavía es el día anterior", () => {
    expect(hoyColombiaIso(new Date("2026-10-28T02:00:00Z"))).toBe("2026-10-27");
    expect(hoyColombiaIso(new Date("2026-10-28T05:00:00Z"))).toBe("2026-10-28");
  });
});

describe("agruparTarifas", () => {
  it("junta los conceptos de un mismo tipo, organismo y fecha de inicio y toma el mejor estado", () => {
    const grupos = agruparTarifas([
      tarifa({ id: "a", concepto: "DERECHOS_TRANSITO" }),
      tarifa({ id: "b", concepto: "SERVICIO_FLIT", valorCop: 50000, estado: "cerrada" }),
      tarifa({ id: "c", concepto: "DERECHOS_TRANSITO", vigenteDesde: "2027-01-01", estado: "futura" }),
      tarifa({ id: "d", organismoId: "org-1", organismoNombre: "Bogotá" }),
    ]);
    expect(grupos).toHaveLength(3);
    const general = grupos.find((g) => g.organismoId === null && g.vigenteDesde === "2026-01-01");
    expect(Object.keys(general!.porConcepto).sort()).toEqual(["DERECHOS_TRANSITO", "SERVICIO_FLIT"]);
    expect(general!.estado).toBe("vigente");
    expect(grupos.find((g) => g.vigenteDesde === "2027-01-01")?.estado).toBe("futura");
  });
});
