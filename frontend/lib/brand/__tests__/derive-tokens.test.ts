// Paleta derivada de una Marca Blanca (ADR-0060 §D5): el configurador elige solo el color de texto,
// corrige combinaciones ilegibles y BrandStyle deriva degradado, fondos y tinta del color de acción.
import { describe, expect, it } from "vitest";
import { contrastRatio, MIN_CONTRAST_RATIO } from "@/lib/brand/contrast";
import {
  brandColorsReadable,
  deriveBrandSurfaces,
  fixContrast,
  mixWithWhite,
  pickOnPrimary,
} from "@/lib/brand/derive-tokens";
import { BRAND_COLOR_PRESETS } from "@/components/admin/branding/brand-color-presets";

describe("derive-tokens", () => {
  it("toda combinación sugerida se lee bien con el texto que se elige solo", () => {
    for (const preset of BRAND_COLOR_PRESETS) {
      const onPrimary = pickOnPrimary(preset.primary, preset.secondary);
      expect(brandColorsReadable({ ...preset, onPrimary }), preset.name).toBe(true);
    }
  });

  it("elige texto oscuro sobre colores claros y blanco sobre oscuros", () => {
    expect(pickOnPrimary("#1E3A8A", "#2563EB")).toBe("#FFFFFF");
    expect(pickOnPrimary("#FFE680", "#FFD400")).toBe("#162744");
  });

  it("corrige una combinación ilegible ajustando solo la luminosidad", () => {
    const fixed = fixContrast({ primary: "#557EFF", secondary: "#00DBD5" });
    expect(brandColorsReadable(fixed)).toBe(true);
    // El que ya cumplía no se toca.
    const untouched = fixContrast({ primary: "#1E3A8A", secondary: "#00DBD5" });
    expect(untouched.primary).toBe("#1E3A8A");
  });

  it("la tinta derivada es legible sobre blanco y los fondos son tintes claros", () => {
    const s = deriveBrandSurfaces("#00DBD5");
    expect(contrastRatio(s.ink, "#FFFFFF")).toBeGreaterThanOrEqual(MIN_CONTRAST_RATIO);
    expect(contrastRatio(s.tint, "#FFFFFF")).toBeLessThan(1.2);
    expect(mixWithWhite("#000000", 0.5)).toBe("#808080");
  });
});
