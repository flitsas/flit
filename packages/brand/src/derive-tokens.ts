// Paleta completa de una Marca Blanca a partir de sus dos colores (ADR-0060 §D5). La marca solo
// guarda `primary` (identidad: títulos, encabezados), `secondary` (acción: botones, enlaces, menú
// activo) y `onPrimary` (texto sobre ellos); el resto de la plataforma necesita además el cierre
// del degradado, los fondos tintados y una tinta del color de acción legible como texto pequeño.
// Se derivan aquí, con la misma fórmula WCAG que el backend, para que `BrandStyle` y el
// configurador (que muestra cómo quedará) calculen lo mismo.
//
// Uso de ejemplo:
//   deriveBrandSurfaces("#0E7C86")       // → { accent, bg, tint, ink } para modo claro
//   pickOnPrimary("#0B3D91", "#3D63DD")  // → "#FFFFFF"
//   fixContrast({ primary, secondary, onPrimary }) // → colores ajustados que cumplen 4.5:1
import { contrastRatio, hexToRgb, isValidHexColor, MIN_CONTRAST_RATIO } from "./contrast";
import { hexToOklch, oklchToHex } from "./derive-dark";

const WHITE = "#FFFFFF";
/** Tinta de FLIT: la alternativa oscura al blanco como color de texto sobre la marca. */
const INK = "#162744";

/** Mezcla `hex` con blanco: `weight` = proporción del color (0..1). */
export function mixWithWhite(hex: string, weight: number): string {
  const { r, g, b } = hexToRgb(hex);
  const ch = (c: number) => Math.round(255 - (255 - c) * weight);
  const toHex = (n: number) => n.toString(16).padStart(2, "0");
  return `#${toHex(ch(r))}${toHex(ch(g))}${toHex(ch(b))}`.toUpperCase();
}

/**
 * Baja la luminosidad OKLCH de `hex` (mismo croma y matiz) hasta que llega a `minContrast` sobre
 * `background`. Si ya cumple se devuelve tal cual.
 */
export function deriveInkTone(
  hex: string,
  background: string = WHITE,
  minContrast: number = MIN_CONTRAST_RATIO,
): string {
  const normalized = hex.toUpperCase();
  if (contrastRatio(normalized, background) >= minContrast) return normalized;
  const base = hexToOklch(normalized);
  for (let l = base.l - 0.02; l > 0; l -= 0.02) {
    const candidate = oklchToHex({ ...base, l });
    if (contrastRatio(candidate, background) >= minContrast) return candidate;
  }
  return INK;
}

/** Cierre del degradado de los botones y del menú activo: el mismo matiz, más luminoso. */
export function deriveAccentTone(hex: string): string {
  const base = hexToOklch(hex);
  return oklchToHex({ ...base, l: Math.min(0.9, base.l + 0.14) });
}

export interface BrandSurfaces {
  /** Cierre del degradado (en FLIT, #00DBD5). */
  accent: string;
  /** Fondo de página tintado (en FLIT, #EEF5FF). */
  bg: string;
  /** Fondo tintado de menús, chips y paneles (en FLIT, #EAF2FF). */
  tint: string;
  /** El color de acción legible como texto pequeño sobre blanco (en FLIT, #476BD9). */
  ink: string;
}

/** Tonos de modo claro derivados del color de acción (`secondary`). */
export function deriveBrandSurfaces(secondary: string): BrandSurfaces {
  return {
    accent: deriveAccentTone(secondary),
    bg: mixWithWhite(secondary, 0.07),
    tint: mixWithWhite(secondary, 0.12),
    ink: deriveInkTone(secondary),
  };
}

/** Peor contraste de `text` sobre los dos colores de marca (los dos pares que valida el backend). */
function worstContrast(text: string, primary: string, secondary: string): number {
  return Math.min(contrastRatio(text, primary), contrastRatio(text, secondary));
}

/**
 * Elige el color de texto sobre la marca: blanco o la tinta de FLIT, el que se lea mejor sobre
 * los dos colores a la vez. Así quien configura no tiene que pensar en el contraste del texto.
 */
export function pickOnPrimary(primary: string, secondary: string): string {
  if (!isValidHexColor(primary) || !isValidHexColor(secondary)) return WHITE;
  return worstContrast(WHITE, primary, secondary) >= worstContrast(INK, primary, secondary) ? WHITE : INK;
}

export interface BrandColorsInput {
  primary: string;
  secondary: string;
  onPrimary: string;
}

/** `true` si los dos pares (texto/principal y texto/secundario) cumplen 4.5:1. */
export function brandColorsReadable(colors: BrandColorsInput): boolean {
  if (![colors.primary, colors.secondary, colors.onPrimary].every(isValidHexColor)) return false;
  return worstContrast(colors.onPrimary, colors.primary, colors.secondary) >= MIN_CONTRAST_RATIO;
}

/**
 * Corrige una combinación que no se lee bien: oscurece (texto blanco) o aclara (texto oscuro) solo
 * el color que falla, conservando su matiz, hasta llegar a 4.5:1. Devuelve los colores con el
 * texto ya elegido; si ya cumplían, los mismos.
 */
export function fixContrast(colors: { primary: string; secondary: string }): BrandColorsInput {
  const onPrimary = pickOnPrimary(colors.primary, colors.secondary);
  const adjust = (hex: string) => {
    if (contrastRatio(onPrimary, hex) >= MIN_CONTRAST_RATIO) return hex.toUpperCase();
    const base = hexToOklch(hex);
    const step = onPrimary === WHITE ? -0.02 : 0.02;
    for (let l = base.l + step; l > 0 && l < 1; l += step) {
      const candidate = oklchToHex({ ...base, l });
      if (contrastRatio(onPrimary, candidate) >= MIN_CONTRAST_RATIO) return candidate;
    }
    return onPrimary === WHITE ? INK : WHITE;
  };
  return { primary: adjust(colors.primary), secondary: adjust(colors.secondary), onPrimary };
}
