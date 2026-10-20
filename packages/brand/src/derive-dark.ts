// Derivación de tonos de modo oscuro a partir de un color de marca (HU #12419 AC4, ADR-0060
// §D5). Conversión hex → OKLCH → hex propia (sin dependencia nueva, ~90 líneas): fórmulas de
// Björn Ottosson para OKLab (https://bottosson.github.io/posts/oklab/) + coordenadas polares
// para el croma/matiz de OKLCH. Se sube SOLO la luminosidad (L) manteniendo croma y matiz, y se
// valida el resultado con `contrastRatio` de `lib/brand/contrast.ts` — LA MISMA fórmula WCAG 2.1
// que usa el backend (`WcagContrast.cs`, #12413) — hasta alcanzar el mínimo de accesibilidad.
//
// Uso de ejemplo:
//   deriveDarkTone("#0B3D91")                        // → tono más claro, ≥4.5:1 sobre #05060A
//   deriveDarkPalette({ primary, secondary, onPrimary })
import { contrastRatio, hexToRgb, MIN_CONTRAST_RATIO, type Rgb } from "./contrast";

export interface Oklch {
  /** Luminosidad perceptual, 0..1. */
  l: number;
  /** Croma (saturación perceptual), típicamente 0..0.4. */
  c: number;
  /** Matiz en grados, 0..360. */
  h: number;
}

function srgbToLinear(channel255: number): number {
  const c = channel255 / 255;
  return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
}

function linearToSrgb255(linear: number): number {
  const clamped = Math.min(1, Math.max(0, linear));
  const v = clamped <= 0.0031308 ? clamped * 12.92 : 1.055 * Math.pow(clamped, 1 / 2.4) - 0.055;
  return Math.round(Math.min(1, Math.max(0, v)) * 255);
}

function rgbToOklab(rgb: Rgb): { l: number; a: number; b: number } {
  const r = srgbToLinear(rgb.r);
  const g = srgbToLinear(rgb.g);
  const b = srgbToLinear(rgb.b);

  const l = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b;
  const m = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b;
  const s = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b;

  const l_ = Math.cbrt(l);
  const m_ = Math.cbrt(m);
  const s_ = Math.cbrt(s);

  return {
    l: 0.2104542553 * l_ + 0.793617785 * m_ - 0.0040720468 * s_,
    a: 1.9779984951 * l_ - 2.428592205 * m_ + 0.4505937099 * s_,
    b: 0.0259040371 * l_ + 0.7827717662 * m_ - 0.808675766 * s_,
  };
}

function oklabToRgb(lab: { l: number; a: number; b: number }): Rgb {
  const l_ = lab.l + 0.3963377774 * lab.a + 0.2158037573 * lab.b;
  const m_ = lab.l - 0.1055613458 * lab.a - 0.0638541728 * lab.b;
  const s_ = lab.l - 0.0894841775 * lab.a - 1.291485548 * lab.b;

  const l = l_ * l_ * l_;
  const m = m_ * m_ * m_;
  const s = s_ * s_ * s_;

  return {
    r: linearToSrgb255(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s),
    g: linearToSrgb255(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s),
    b: linearToSrgb255(-0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s),
  };
}

function rgbToHex(rgb: Rgb): string {
  const toHex = (n: number) => n.toString(16).padStart(2, "0");
  return `#${toHex(rgb.r)}${toHex(rgb.g)}${toHex(rgb.b)}`.toUpperCase();
}

/** `#RRGGBB` → OKLCH. */
export function hexToOklch(hex: string): Oklch {
  const lab = rgbToOklab(hexToRgb(hex));
  const c = Math.sqrt(lab.a * lab.a + lab.b * lab.b);
  let h = (Math.atan2(lab.b, lab.a) * 180) / Math.PI;
  if (h < 0) h += 360;
  return { l: lab.l, c, h };
}

/** OKLCH → `#RRGGBB` (recorta al gamut sRGB vía `linearToSrgb255`). */
export function oklchToHex(oklch: Oklch): string {
  const hRad = (oklch.h * Math.PI) / 180;
  return rgbToHex(oklabToRgb({ l: oklch.l, a: Math.cos(hRad) * oklch.c, b: Math.sin(hRad) * oklch.c }));
}

/** Fondo oscuro de referencia — el mismo que pinta `Shell.tsx` en modo oscuro (`dark ? "#05060A" : ...`). */
export const DARK_APP_BACKGROUND = "#05060A";

const LIGHTNESS_STEP = 0.02;
const MAX_ITERATIONS = 40;

/**
 * Sube la luminosidad OKLCH de `hex` (manteniendo croma y matiz) hasta que el contraste contra
 * `background` alcanza `minContrast` (WCAG 2.1 AA, 4.5:1). Si `hex` YA cumple, se devuelve tal
 * cual — un color de marca que ya es legible en oscuro no debería aclararse de más.
 */
export function deriveDarkTone(
  hex: string,
  background: string = DARK_APP_BACKGROUND,
  minContrast: number = MIN_CONTRAST_RATIO,
): string {
  const normalized = hex.toUpperCase();
  if (contrastRatio(normalized, background) >= minContrast) {
    return normalized;
  }

  const base = hexToOklch(normalized);
  let best = normalized;
  let bestRatio = contrastRatio(normalized, background);

  for (let i = 1; i <= MAX_ITERATIONS; i++) {
    const l = Math.min(1, base.l + i * LIGHTNESS_STEP);
    const candidate = oklchToHex({ ...base, l });
    const ratio = contrastRatio(candidate, background);
    if (ratio > bestRatio) {
      best = candidate;
      bestRatio = ratio;
    }
    if (ratio >= minContrast) {
      return candidate;
    }
    if (l >= 1) break;
  }

  // Mejor esfuerzo: un color de marca extremo puede no alcanzar 4.5:1 ni en L=1 (p. ej. un
  // amarillo puro). Se devuelve el mejor candidato encontrado en vez de romper el render.
  return best;
}

export interface DerivedDarkPalette {
  primary: string;
  secondary: string;
  onPrimary: string;
}

/** Deriva la paleta de modo oscuro completa de una marca (HU #12419 AC4). `onPrimary` no se
 * deriva: es el color de TEXTO sobre `primary`, cuyo contraste ya lo valida el backend contra el
 * `primary` claro (#12413); en oscuro el texto vuelve a evaluarse contra el `primary` derivado en
 * el componente que lo consuma, no aquí. */
export function deriveDarkPalette(colors: DerivedDarkPalette): DerivedDarkPalette {
  return {
    primary: deriveDarkTone(colors.primary),
    secondary: deriveDarkTone(colors.secondary),
    onPrimary: colors.onPrimary,
  };
}
