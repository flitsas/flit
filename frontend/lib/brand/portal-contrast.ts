// Épica #14297 — Reglas de lectura de la marca del portal del organismo. Marca blanca solo revisa el texto sobre los
// colores; en el portal el color principal además se usa como texto (nombre, títulos y enlaces) sobre su fondo claro,
// así que también debe leerse ahí. La misma regla la valida el servidor al publicar (PortalMarcaReglas).
import type { BrandColors } from "@/lib/api/branding";
import { brandContrastPairs, contrastRatio, isValidHexColor } from "@/lib/brand/contrast";
import { deriveInkTone } from "@/lib/brand/derive-tokens";

/** Fondo de página del portal (`--flit-soft` de frontend-personanatural) en #RRGGBB. */
export const FONDO_PORTAL = "#E6EFFC";
/** WCAG 1.4.3 para texto grande y títulos en negrita. */
export const CONTRASTE_TITULOS = 3;

const BLANCO = "#FFFFFF";
const CONTRASTE_TEXTO = 4.5;

export const MENSAJE_COLORES_ILEGIBLES =
  "Con estos colores el nombre, los títulos o los botones del portal no se leerían bien. Puedes corregirlo con un clic: oscurecemos el tono sin cambiar el color.";

/** `true` si el color principal se lee como título sobre el fondo del portal. */
export function titulosLegibles(colors: BrandColors): boolean {
  return isValidHexColor(colors.primary) && contrastRatio(colors.primary, FONDO_PORTAL) >= CONTRASTE_TITULOS;
}

/** Todos los pares que usa el portal: texto sobre los colores (4,5:1) y principal sobre el fondo (3:1). */
export function coloresPortalLegibles(colors: BrandColors): boolean {
  return brandContrastPairs(colors).every((par) => par.ok) && titulosLegibles(colors);
}

/**
 * Oscurece el tono, sin cambiar el matiz, hasta que el texto blanco se lea sobre los dos colores. Con ese contraste el
 * principal también cumple sobre el fondo claro. Si el color ya cumple, se conserva.
 */
export function ajustarColoresPortal(colors: Pick<BrandColors, "primary" | "secondary">): BrandColors {
  return {
    primary: deriveInkTone(colors.primary, BLANCO, CONTRASTE_TEXTO),
    secondary: deriveInkTone(colors.secondary, BLANCO, CONTRASTE_TEXTO),
    onPrimary: BLANCO,
  };
}

/** Par adicional para el «Detalle técnico» del selector de colores. */
export function parTitulos(colors: BrandColors) {
  const valido = isValidHexColor(colors.primary);
  return {
    label: "del color principal sobre el fondo del portal",
    ratio: valido ? contrastRatio(colors.primary, FONDO_PORTAL) : null,
    ok: titulosLegibles(colors),
    minimo: "3:1",
  };
}
