// HU #12723 AC1 / AC6 — token --flit-brand-ink con contraste WCAG AA documentado.
// Uso de ejemplo: contrastRatio("#476BD9", "#FFFFFF") ≥ 4.5; --color-flit-brand sigue #557eff.
import { describe, expect, it } from "vitest";
import { contrastRatio, MIN_CONTRAST_RATIO } from "../contrast";
import { readDesignCss } from "../../../__tests__/helpers/read-design-css";

const GLOBALS = readDesignCss();

/** Fondo del dock en tema oscuro (comentario HU #12723 junto al token dark). */
const DARK_DOCK_BG = "#162744";
const LIGHT_BG = "#FFFFFF";
const BRAND_FILL = "#557eff";

function extractBrandInkTokens(css: string): { light: string; dark: string } {
  const lightMatch = css.match(
    /\/\* Tinta legible del azul de marca[\s\S]*?--flit-brand-ink:\s*(?:var\(--brand-ink,\s*)?(#[0-9A-Fa-f]{6})\)?\s*;/,
  );
  const darkBlock = css.split(/\.dark\s*\{/)[1] ?? "";
  const darkMatch = darkBlock.match(/--flit-brand-ink:\s*(?:var\(--brand-ink,\s*)?(#[0-9A-Fa-f]{6})\)?\s*;/);
  if (!lightMatch?.[1] || !darkMatch?.[1]) {
    throw new Error("No se encontraron ambos tokens --flit-brand-ink (claro/oscuro) en globals.css");
  }
  return { light: lightMatch[1], dark: darkMatch[1] };
}

describe("HU #12723 — --flit-brand-ink (AC1)", () => {
  it("happy path: tinta clara ≥ 4,5:1 sobre blanco y comentario documenta ratio", () => {
    const { light } = extractBrandInkTokens(GLOBALS);
    // UI Kit 2026 (propuesta-2): el azul de texto y botones #1d53ff (antes #3d5fd9 de «Diseño Inteligente»).
    expect(light.toLowerCase()).toBe("#1d53ff");
    expect(contrastRatio(light, LIGHT_BG)).toBeGreaterThanOrEqual(MIN_CONTRAST_RATIO);
    expect(GLOBALS).toMatch(/5,62:1|5\.62:1/);
  });

  it("edge: tinta dark ≥ 4,5:1 sobre fondo dock oscuro", () => {
    const { dark } = extractBrandInkTokens(GLOBALS);
    // UI Kit 2026: en oscuro el azul de texto es #8fb0ff.
    expect(dark.toLowerCase()).toBe("#8fb0ff");
    expect(contrastRatio(dark, DARK_DOCK_BG)).toBeGreaterThanOrEqual(MIN_CONTRAST_RATIO);
    expect(GLOBALS).toMatch(/6,98:1|6\.98:1/);
  });

  it("contrato: --color-flit-brand fallback #557eff no cambia; ink ≠ fill", () => {
    expect(GLOBALS).toMatch(/--color-flit-brand:\s*var\(--brand-secondary,\s*#557eff\)/);
    const { light } = extractBrandInkTokens(GLOBALS);
    expect(light.toLowerCase()).not.toBe(BRAND_FILL.toLowerCase());
    expect(contrastRatio(BRAND_FILL, LIGHT_BG)).toBeLessThan(MIN_CONTRAST_RATIO);
  });
});

describe("Dock — ítem seleccionado con el degradado del isotipo central", () => {
  it("píldora, ancestro y subítem activos usan --nav-activo e icono blanco", () => {
    expect(GLOBALS).toMatch(
      // UI Kit 2026 (propuesta-2): el activo del dock es azul plano (sin degradado), con la tinta de la marca si la hay.
      /--nav-activo:\s*var\(--brand-ink,\s*#1d53ff\)/i,
    );
    // UI kit (HU #14474): en FLIT el degradado del ítem activo es #557EFF → #0F6FA8 (cierre legible con texto
    // blanco); en una Marca Blanca cierra en su `--brand-accent`. El turquesa sigue como acento decorativo.
    expect(GLOBALS).toMatch(/--color-flit-tech:\s*var\(--brand-accent,\s*#00dbd5\)/i);
    expect(GLOBALS).toMatch(
      /\.dock-pill\[aria-current="page"\],\s*\.dock-pill\[data-ancestor-active="true"\]\s*\{[^}]*background:\s*var\(--nav-activo\)/,
    );
    expect(GLOBALS).toMatch(
      /\.dock-pill\[aria-current="page"\] > svg,\s*\.dock-pill\[data-ancestor-active="true"\] > svg\s*\{[^}]*color:\s*#ffffff/,
    );
    // HU #14479 (propuesta): la entrada activa del submenú va en el azul suave de acción, no en el degradado.
    expect(GLOBALS).toMatch(
      /\.dock-entrada\[aria-current="page"\]\s*\{[^}]*background:\s*var\(--di-accent-soft\)/,
    );
  });

  it("el hover del activo conserva el degradado; .dock-fab activo lleva el aro turquesa de la propuesta", () => {
    expect(GLOBALS).toMatch(
      /\.dock-pill\[aria-current="page"\]:hover[\s\S]*?background:\s*var\(--nav-activo\)/,
    );
    expect(GLOBALS).toMatch(/\.dock-fab\[aria-current="page"\]\s*\{[^}]*outline:\s*3px solid var\(--di-highlight-decor\)/);
  });
});
