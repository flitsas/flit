// HU #12414 AC3 — contraste WCAG en vivo, misma fórmula que reutilizará #12419.
import { describe, expect, it } from "vitest";
import {
  contrastRatio,
  formatContrastRatio,
  hexToRgb,
  isValidHexColor,
  meetsMinimumContrast,
  relativeLuminance,
  MIN_CONTRAST_RATIO,
} from "../contrast";

describe("hexToRgb", () => {
  it("parsea #RRGGBB a componentes 0-255", () => {
    expect(hexToRgb("#FFFFFF")).toEqual({ r: 255, g: 255, b: 255 });
    expect(hexToRgb("#000000")).toEqual({ r: 0, g: 0, b: 0 });
  });

  it("lanza con un formato inválido", () => {
    expect(() => hexToRgb("azul")).toThrow();
  });
});

describe("isValidHexColor", () => {
  it("acepta #RRGGBB en mayúsculas y minúsculas", () => {
    expect(isValidHexColor("#557EFF")).toBe(true);
    expect(isValidHexColor("#557eff")).toBe(true);
  });

  it("rechaza formatos cortos o sin #", () => {
    expect(isValidHexColor("#FFF")).toBe(false);
    expect(isValidHexColor("557EFF")).toBe(false);
    expect(isValidHexColor("")).toBe(false);
  });
});

describe("relativeLuminance / contrastRatio", () => {
  it("blanco sobre negro da el contraste máximo (21:1)", () => {
    expect(relativeLuminance("#FFFFFF")).toBeCloseTo(1, 5);
    expect(relativeLuminance("#000000")).toBeCloseTo(0, 5);
    expect(contrastRatio("#FFFFFF", "#000000")).toBeCloseTo(21, 0);
  });

  it("es simétrico entre los dos colores", () => {
    expect(contrastRatio("#0B3D91", "#FFFFFF")).toBeCloseTo(contrastRatio("#FFFFFF", "#0B3D91"), 5);
  });

  it("mismo color da contraste 1:1", () => {
    expect(contrastRatio("#557EFF", "#557EFF")).toBeCloseTo(1, 5);
  });
});

describe("meetsMinimumContrast", () => {
  it("cumple con un ratio alto (contrato: pares con contraste suficiente publican)", () => {
    const ratio = contrastRatio("#0B3D91", "#FFFFFF");
    expect(meetsMinimumContrast(ratio)).toBe(true);
    expect(ratio).toBeGreaterThanOrEqual(MIN_CONTRAST_RATIO);
  });

  it("no cumple con un contraste insuficiente (AC3: puede guardar, no publicar)", () => {
    const ratio = contrastRatio("#557EFF", "#4F74C9");
    expect(meetsMinimumContrast(ratio)).toBe(false);
  });
});

describe("formatContrastRatio", () => {
  it("redondea a 2 decimales", () => {
    expect(formatContrastRatio(4.5001)).toBe("4.50");
    expect(formatContrastRatio(21)).toBe("21.00");
  });
});
