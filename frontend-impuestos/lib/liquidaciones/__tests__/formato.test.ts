import { describe, expect, it } from "vitest";
import {
  SIN_DATO,
  formatoCifra,
  formatoDocumento,
  formatoFecha,
  formatoFechaDia,
  formatoMonto,
  idCorto,
  normalizarPlaca,
  placaValida,
} from "@/lib/liquidaciones/formato";

// Uso de ejemplo: formatoMonto(null) → «Sin dato»; formatoMonto(0) → «$ 0»; formatoMonto(1234567) → «$ 1.234.567».
// Un monto null significa «sin dato» y nunca se pinta como 0 (contrato impuestos.v1.yaml, F2b-CF7).

/** Intl pone un espacio duro entre el símbolo y la cifra; las aserciones lo comparan como espacio normal. */
const plano = (s: string) => s.replace(/ /g, " ");

describe("formatoMonto (F2b-CF7: null ≠ 0)", () => {
  it("null y undefined dicen «Sin dato», nunca 0 ni guion", () => {
    expect(formatoMonto(null)).toBe("Sin dato");
    expect(formatoMonto(undefined)).toBe(SIN_DATO);
    expect(formatoMonto(null)).not.toMatch(/0|—/);
  });

  it("0 es un dato real: «$ 0»", () => {
    expect(plano(formatoMonto(0))).toBe("$ 0");
  });

  it("pesos colombianos sin decimales de relleno y con separador de miles es-CO", () => {
    expect(plano(formatoMonto(1234567))).toBe("$ 1.234.567");
    expect(plano(formatoMonto(1500.5))).toBe("$ 1.500,5");
  });

  it("un valor no numérico (NaN) también es «Sin dato»", () => {
    expect(formatoMonto(Number.NaN)).toBe(SIN_DATO);
  });
});

describe("formatoFecha y formatoFechaDia", () => {
  it("dd/mm/aaaa hh:mm en hora de Colombia", () => {
    expect(formatoFecha("2026-11-24T19:05:00Z")).toBe("24/11/2026 14:05");
  });

  it("null o inválida dicen «Sin dato»", () => {
    expect(formatoFecha(null)).toBe(SIN_DATO);
    expect(formatoFecha("no-es-fecha")).toBe(SIN_DATO);
  });

  it("una fecha de calendario (aaaa-mm-dd) no se corre de día por la zona", () => {
    expect(formatoFechaDia("2026-03-01")).toBe("01/03/2026");
    expect(formatoFechaDia(null)).toBe(SIN_DATO);
  });
});

describe("placa, documento, cifras e id", () => {
  it("normaliza la placa como el servidor: mayúsculas, sin espacios ni guiones", () => {
    expect(normalizarPlaca(" abc-123 ")).toBe("ABC123");
  });

  it("placa válida: de 4 a 10 letras o números", () => {
    expect(placaValida("ABC123")).toBe(true);
    expect(placaValida("AB1")).toBe(false);
    expect(placaValida("ABC12345678")).toBe(false);
    expect(placaValida("AB#123")).toBe(false);
  });

  it("documento «{tipo} {número}» sin máscara; sin número, «Sin dato»", () => {
    expect(formatoDocumento("CC", "0000000")).toBe("CC 0000000");
    expect(formatoDocumento(null, null)).toBe(SIN_DATO);
    expect(formatoDocumento(null, "0000000")).toBe("0000000");
  });

  it("cifras es-CO y id corto en mayúsculas", () => {
    expect(formatoCifra(1234)).toBe("1.234");
    expect(idCorto("0193abcd-0000-7000-8000-000000000001")).toBe("0193ABCD");
  });
});
