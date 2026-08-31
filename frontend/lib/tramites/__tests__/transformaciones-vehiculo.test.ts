// HU #11931 — la regla de «hay transformación» es compartida por el asistente del gestor (que
// captura el valor nuevo) y el detalle del OT (que lo revisa), así que se prueba aparte de ambos.
import { describe, expect, it } from "vitest";
import {
  transformacionesDeclaradas,
  valorCambiado,
} from "../transformaciones-vehiculo";

describe("valorCambiado", () => {
  it("detecta el cambio ignorando mayúsculas y espacios sobrantes", () => {
    expect(valorCambiado("PLATA", "ROJO")).toBe(true);
    expect(valorCambiado("plata", " PLATA ")).toBe(false);
  });

  it("sin alguna de las dos caras no declara nada", () => {
    // Un atributo a medio capturar no es una transformación: sin RUNT no hay contra qué comparar,
    // y sin valor efectivo no hay cambio que declarar.
    expect(valorCambiado(null, "ROJO")).toBe(false);
    expect(valorCambiado("PLATA", "")).toBe(false);
    expect(valorCambiado(undefined, undefined)).toBe(false);
  });
});

describe("transformacionesDeclaradas", () => {
  it("declara por diferencia entre el RUNT y el valor efectivo", () => {
    const result = transformacionesDeclaradas([
      { tipo: "color", valorRunt: "PLATA", valorEfectivo: "ROJO" },
    ]);

    expect(result).toEqual([
      { tipo: "color", label: "Color", valorRunt: "PLATA", valorNuevo: "ROJO" },
    ]);
  });

  it("declara por bandera aunque el valor nuevo todavía no esté capturado", () => {
    const result = transformacionesDeclaradas([
      { tipo: "combustible", valorRunt: "GASOLINA", valorEfectivo: null, declarado: true },
    ]);

    expect(result).toHaveLength(1);
    expect(result[0].valorNuevo).toBeNull();
    expect(result[0].valorRunt).toBe("GASOLINA");
  });

  it("no inventa el valor del RUNT cuando el trámite no lo consultó", () => {
    const result = transformacionesDeclaradas([
      { tipo: "carroceria", valorRunt: null, valorEfectivo: "ESTACAS", declarado: true },
    ]);

    expect(result[0].valorRunt).toBeNull();
    expect(result[0].valorNuevo).toBe("ESTACAS");
  });

  it("descarta los atributos sin transformación y conserva el orden de los demás", () => {
    const result = transformacionesDeclaradas([
      { tipo: "color", valorRunt: "PLATA", valorEfectivo: "ROJO" },
      { tipo: "combustible", valorRunt: "GASOLINA", valorEfectivo: "GASOLINA" },
      { tipo: "carroceria", valorRunt: "SEDAN", valorEfectivo: "ESTACAS" },
    ]);

    expect(result.map((t) => t.tipo)).toEqual(["color", "carroceria"]);
  });

  it("usa el mismo vocabulario de atributos que el módulo de Reportes", () => {
    const result = transformacionesDeclaradas([
      { tipo: "color", declarado: true },
      { tipo: "combustible", declarado: true },
      { tipo: "carroceria", declarado: true },
    ]);

    expect(result.map((t) => t.label)).toEqual(["Color", "Combustible", "Carrocería"]);
  });
});
