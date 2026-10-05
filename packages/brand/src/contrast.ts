// Contraste WCAG 2.1 propio (sin dependencia nueva) — HU #12414 AC3.
// Misma fórmula que reutilizará el resolutor de marca del lado servidor (#12419,
// ADR-0060 §D5 `lib/brand/derive-dark.ts`): luminancia relativa sRGB → ratio de contraste.
//
// Uso de ejemplo:
//   contrastRatio("#0B3D91", "#FFFFFF") // → 8.35
//   meetsMinimumContrast(8.35) // → true

/** Mínimo WCAG 2.1 AA para texto normal (criterio 1.4.3). */
export const MIN_CONTRAST_RATIO = 4.5;

export interface Rgb {
  r: number;
  g: number;
  b: number;
}

const HEX_PATTERN = /^#([0-9A-Fa-f]{6})$/;

/** Parsea `#RRGGBB` a componentes 0-255. Lanza si el formato no es válido. */
export function hexToRgb(hex: string): Rgb {
  const match = HEX_PATTERN.exec(hex.trim());
  if (!match) {
    throw new Error(`Color hexadecimal inválido: "${hex}"`);
  }
  const value = match[1];
  return {
    r: parseInt(value.slice(0, 2), 16),
    g: parseInt(value.slice(2, 4), 16),
    b: parseInt(value.slice(4, 6), 16),
  };
}

/** `true` si el string cumple `^#[0-9A-Fa-f]{6}$` (mismo patrón que el backend). */
export function isValidHexColor(hex: string): boolean {
  return HEX_PATTERN.test(hex.trim());
}

function srgbChannelToLinear(channel: number): number {
  const c = channel / 255;
  return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
}

/** Luminancia relativa WCAG (0..1) de un color `#RRGGBB`. */
export function relativeLuminance(hex: string): number {
  const { r, g, b } = hexToRgb(hex);
  const [rl, gl, bl] = [r, g, b].map(srgbChannelToLinear);
  return 0.2126 * rl + 0.7152 * gl + 0.0722 * bl;
}

/** Ratio de contraste WCAG entre dos colores `#RRGGBB` (1..21). */
export function contrastRatio(hexA: string, hexB: string): number {
  const l1 = relativeLuminance(hexA);
  const l2 = relativeLuminance(hexB);
  const lighter = Math.max(l1, l2);
  const darker = Math.min(l1, l2);
  return (lighter + 0.05) / (darker + 0.05);
}

/** `true` si el ratio cumple el mínimo WCAG AA (4.5:1 por defecto). */
export function meetsMinimumContrast(ratio: number, min: number = MIN_CONTRAST_RATIO): boolean {
  return ratio >= min;
}

/** Ratio redondeado a 2 decimales, para mostrar en UI ("4.52"). */
export function formatContrastRatio(ratio: number): string {
  return ratio.toFixed(2);
}
