// HU #12209 (Feature #12201) — DV del NIT por módulo 11 DIAN, solo visual en cliente.
// Uso de ejemplo: calcularDigitoVerificacion("901555444") → "4".
import { describe, expect, it } from "vitest";
import { calcularDigitoVerificacion } from "../nit-dv";

describe("calcularDigitoVerificacion — módulo 11 DIAN", () => {
  /** Happy path: NIT de 9 dígitos, el formato habitual de una persona jurídica colombiana. */
  it("calcula el DV de un NIT de nueve dígitos", () => {
    // Vector externo verificable: 890.903.938-8 es un NIT público de persona jurídica (sin PII).
    // Es el único caso cuyo DV NO se deriva de esta implementación, así que es el que realmente
    // ancla el algoritmo; el resto de aserciones comprueban propiedades estructurales.
    expect(calcularDigitoVerificacion("890903938")).toBe("8");
  });

  /** Los residuos 0 y 1 son el propio DV: complementarlos a 11 daría un dígito imposible. */
  it("devuelve el residuo tal cual cuando vale 0 o 1", () => {
    const dvs = new Set<string>();
    for (let n = 1; n <= 400; n += 1) {
      const dv = calcularDigitoVerificacion(String(n));
      if (dv) dvs.add(dv);
    }
    expect(dvs.has("0")).toBe(true);
    expect(dvs.has("1")).toBe(true);
    // Y nunca sale un "11" ni un "10": el DV es un solo dígito.
    for (const dv of dvs) expect(dv).toMatch(/^[0-9]$/);
  });

  /** Tolera el formato con puntos y guiones que el usuario copia del RUT. */
  it("ignora puntos, guiones y espacios", () => {
    expect(calcularDigitoVerificacion("890.903.938")).toBe("8");
    expect(calcularDigitoVerificacion(" 890-903-938 ")).toBe("8");
  });

  /** Edge cases: sin dígito no hay DV, y mostrar uno inventado sería peor que no mostrar nada. */
  it("devuelve null cuando el valor no es un NIT utilizable", () => {
    expect(calcularDigitoVerificacion("")).toBeNull();
    expect(calcularDigitoVerificacion(null)).toBeNull();
    expect(calcularDigitoVerificacion(undefined)).toBeNull();
    expect(calcularDigitoVerificacion("ABC123")).toBeNull();
    // Más largo que la tabla de pesos DIAN: no se puede calcular.
    expect(calcularDigitoVerificacion("1234567890123456")).toBeNull();
  });

  /** Contrato: siempre string de un carácter o null. Nunca number, nunca NaN. */
  it("devuelve siempre un string de un dígito o null", () => {
    const dv = calcularDigitoVerificacion("901555444");
    expect(typeof dv).toBe("string");
    expect(dv).toMatch(/^[0-9]$/);
  });
});
