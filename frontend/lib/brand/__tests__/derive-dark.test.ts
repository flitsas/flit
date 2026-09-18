import { describe, expect, it } from "vitest";
import { contrastRatio, MIN_CONTRAST_RATIO } from "../contrast";
import { deriveDarkPalette, deriveDarkTone, hexToOklch, oklchToHex, DARK_APP_BACKGROUND } from "../derive-dark";

/**
 * Uso de ejemplo: `deriveDarkTone("#0B3D91")` sube la luminosidad OKLCH de un azul oscuro hasta
 * que su contraste contra el fondo oscuro de la app (`#05060A`) alcanza 4.5:1 (WCAG 2.1 AA) —
 * misma fórmula que el backend (`WcagContrast.cs`, #12413, fixture compartido).
 */
describe("hexToOklch / oklchToHex — round-trip", () => {
  it("un color conocido conserva su hex tras ida y vuelta (tolerancia de redondeo sRGB)", () => {
    const oklch = hexToOklch("#557EFF");
    const roundTripped = oklchToHex(oklch);
    // Tolerancia: la ida y vuelta por OKLab con redondeo a 8 bits puede desviar ±1 por canal.
    const rt = { r: parseInt(roundTripped.slice(1, 3), 16), g: parseInt(roundTripped.slice(3, 5), 16), b: parseInt(roundTripped.slice(5, 7), 16) };
    const orig = { r: 0x55, g: 0x7e, b: 0xff };
    expect(Math.abs(rt.r - orig.r)).toBeLessThanOrEqual(2);
    expect(Math.abs(rt.g - orig.g)).toBeLessThanOrEqual(2);
    expect(Math.abs(rt.b - orig.b)).toBeLessThanOrEqual(2);
  });

  it("blanco y negro son los extremos de luminosidad OKLCH (contrato de la conversión)", () => {
    expect(hexToOklch("#FFFFFF").l).toBeCloseTo(1, 1);
    expect(hexToOklch("#000000").l).toBeCloseTo(0, 1);
  });
});

describe("deriveDarkTone — HU #12419 AC4", () => {
  it("happy path: un azul de marca oscuro se aclara hasta cumplir 4.5:1 sobre el fondo oscuro", () => {
    const source = "#0B3D91"; // paleta de ejemplo del contrato (contratos-api.md §3)
    expect(contrastRatio(source, DARK_APP_BACKGROUND)).toBeLessThan(MIN_CONTRAST_RATIO);

    const derived = deriveDarkTone(source);

    expect(contrastRatio(derived, DARK_APP_BACKGROUND)).toBeGreaterThanOrEqual(MIN_CONTRAST_RATIO);
  });

  it("edge case: un color que YA cumple el contraste se devuelve sin cambios", () => {
    const alreadyLight = "#E8ECF5"; // casi blanco: contraste altísimo sobre #05060A
    expect(contrastRatio(alreadyLight, DARK_APP_BACKGROUND)).toBeGreaterThanOrEqual(MIN_CONTRAST_RATIO);

    expect(deriveDarkTone(alreadyLight)).toBe(alreadyLight.toUpperCase());
  });

  it("contrato: el resultado siempre es un hex válido de 7 caracteres (#RRGGBB)", () => {
    const derived = deriveDarkTone("#1FA2FF");
    expect(derived).toMatch(/^#[0-9A-F]{6}$/);
  });

  it("mejor esfuerzo: un color extremo que no alcanza 4.5:1 ni en L=1 no revienta y mejora el contraste", () => {
    // Un amarillo puro es intrínsecamente claro (poco margen para "aclarar más" y aun así
    // mantener croma/matiz) — el mejor esfuerzo debe, al menos, no empeorar el contraste original.
    const extreme = "#FFE000";
    const before = contrastRatio(extreme, DARK_APP_BACKGROUND);
    const derived = deriveDarkTone(extreme);
    expect(contrastRatio(derived, DARK_APP_BACKGROUND)).toBeGreaterThanOrEqual(before);
  });
});

describe("deriveDarkPalette — fixture de paletas (AC4)", () => {
  it("deriva primary/secondary y conserva onPrimary sin tocarlo", () => {
    const palette = deriveDarkPalette({ primary: "#0B3D91", secondary: "#1FA2FF", onPrimary: "#FFFFFF" });

    expect(contrastRatio(palette.primary, DARK_APP_BACKGROUND)).toBeGreaterThanOrEqual(MIN_CONTRAST_RATIO);
    expect(contrastRatio(palette.secondary, DARK_APP_BACKGROUND)).toBeGreaterThanOrEqual(MIN_CONTRAST_RATIO);
    expect(palette.onPrimary).toBe("#FFFFFF");
  });

  it("fixture de varias paletas — todas terminan con contraste suficiente (AC4, matriz)", () => {
    const fixtures = [
      { primary: "#162744", secondary: "#557EFF", onPrimary: "#FFFFFF" }, // FLIT, ya oscuro
      { primary: "#E8ECF5", secondary: "#D0D8EE", onPrimary: "#0B3D91" }, // paleta CLARA que obliga a corregir
      { primary: "#8A2BE2", secondary: "#00A86B", onPrimary: "#FFFFFF" }, // saturados intermedios
    ];

    for (const fixture of fixtures) {
      const derived = deriveDarkPalette(fixture);
      expect(contrastRatio(derived.primary, DARK_APP_BACKGROUND)).toBeGreaterThanOrEqual(MIN_CONTRAST_RATIO);
      expect(contrastRatio(derived.secondary, DARK_APP_BACKGROUND)).toBeGreaterThanOrEqual(MIN_CONTRAST_RATIO);
    }
  });
});
