// HU #12723 AC1 / AC6 — token --flit-brand-ink con contraste WCAG AA documentado.
// Uso de ejemplo: contrastRatio("#476BD9", "#FFFFFF") ≥ 4.5; --color-flit-brand sigue #557eff.
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { contrastRatio, MIN_CONTRAST_RATIO } from "../contrast";

const GLOBALS = readFileSync(resolve(__dirname, "../../../app/globals.css"), "utf8");

/** Fondo del dock en tema oscuro (comentario HU #12723 junto al token dark). */
const DARK_DOCK_BG = "#162744";
const LIGHT_BG = "#FFFFFF";
const BRAND_FILL = "#557eff";

function extractBrandInkTokens(css: string): { light: string; dark: string } {
  const lightMatch = css.match(
    /\/\* Tinta legible del azul de marca[\s\S]*?--flit-brand-ink:\s*(#[0-9A-Fa-f]{6})\s*;/,
  );
  const darkBlock = css.split(/\.dark\s*\{/)[1] ?? "";
  const darkMatch = darkBlock.match(/--flit-brand-ink:\s*(#[0-9A-Fa-f]{6})\s*;/);
  if (!lightMatch?.[1] || !darkMatch?.[1]) {
    throw new Error("No se encontraron ambos tokens --flit-brand-ink (claro/oscuro) en globals.css");
  }
  return { light: lightMatch[1], dark: darkMatch[1] };
}

describe("HU #12723 — --flit-brand-ink (AC1)", () => {
  it("happy path: tinta clara ≥ 4,5:1 sobre blanco y comentario documenta ratio", () => {
    const { light } = extractBrandInkTokens(GLOBALS);
    expect(light.toLowerCase()).toBe("#476bd9");
    expect(contrastRatio(light, LIGHT_BG)).toBeGreaterThanOrEqual(MIN_CONTRAST_RATIO);
    expect(GLOBALS).toMatch(/4,80:1|4\.80:1/);
  });

  it("edge: tinta dark ≥ 4,5:1 sobre fondo dock oscuro", () => {
    const { dark } = extractBrandInkTokens(GLOBALS);
    expect(dark.toLowerCase()).toBe("#6fa5ff");
    expect(contrastRatio(dark, DARK_DOCK_BG)).toBeGreaterThanOrEqual(MIN_CONTRAST_RATIO);
    expect(GLOBALS).toMatch(/6,04:1|6\.04:1/);
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
      /--nav-activo:\s*linear-gradient\(90deg,\s*#557eff\s+0%,\s*#00dbd5\s+100%\)/i,
    );
    expect(GLOBALS).toMatch(
      /\.dock-pill\[aria-current="page"\],\s*\.dock-pill\[data-ancestor-active="true"\]\s*\{[^}]*background:\s*var\(--nav-activo\)/s,
    );
    expect(GLOBALS).toMatch(
      /\.dock-pill\[aria-current="page"\] svg,\s*\.dock-pill\[data-ancestor-active="true"\] svg\s*\{[^}]*color:\s*#ffffff/s,
    );
    expect(GLOBALS).toMatch(
      /\.dock-panel-item\[aria-current="page"\]\s*\{[^}]*background:\s*var\(--nav-activo\)/s,
    );
  });

  it("el hover del activo conserva el degradado; .dock-fab usa el mismo token", () => {
    expect(GLOBALS).toMatch(
      /\.dock-pill\[aria-current="page"\]:hover[\s\S]*?background:\s*var\(--nav-activo\)/,
    );
    expect(GLOBALS).toMatch(/\.dock-fab[\s\S]*?--nav-activo|--nav-activo[\s\S]*?\.dock-fab/);
  });
});
