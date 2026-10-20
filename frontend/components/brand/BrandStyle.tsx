// Inyección de variables CSS de marca en <head> ANTES de la primera pintura (HU #12419 AC1,
// ADR-0060 §D5). Server Component puro, sin JS de cliente: evita el destello con los colores de
// FLIT porque el navegador nunca llega a pintar con el fallback antes de aplicar la marca.
//
// En host FLIT NO se renderiza NADA (ni un <style> vacío): `globals.css` calcula los mismos
// valores de siempre vía `var(--brand-primary, <hex FLIT>)` — paridad byte a byte (AC7).
//
// Uso de ejemplo:
//   <head><BrandStyle brand={brand} /></head>
import { deriveDarkPalette } from "@/lib/brand/derive-dark";
import { deriveAccentTone, deriveBrandSurfaces } from "@/lib/brand/derive-tokens";
import { isValidHexColor } from "@/lib/brand/contrast";
import { isFlitBrand, type Brand } from "@/lib/brand/types";

function escapeForCss(hex: string): string {
  // Defensa en profundidad: el backend valida `^#[0-9A-Fa-f]{6}$` (HU #12413), pero si algo
  // llegara con forma inesperada, NUNCA se interpola en el <style> — se cae al color FLIT.
  return isValidHexColor(hex) ? hex : "";
}

export function BrandStyle({ brand }: { brand: Brand }) {
  if (isFlitBrand(brand)) {
    return null;
  }

  const primary = escapeForCss(brand.colors.primary);
  const secondary = escapeForCss(brand.colors.secondary);
  const onPrimary = escapeForCss(brand.colors.onPrimary);

  if (!primary || !secondary || !onPrimary) {
    // Forma inválida pese al guard del resolutor (#12418 ya valida, esto es defensivo) — sin
    // variables de marca, `globals.css` cae a los hex de FLIT (mismo camino que host FLIT).
    return null;
  }

  const dark = deriveDarkPalette({ primary, secondary, onPrimary });
  // Degradado, fondos tintados y tinta de texto salen del color de acción: sin ellos, el menú
  // activo, los fondos y los chips seguían con el azul/verde de FLIT en una red con marca.
  const light = deriveBrandSurfaces(secondary);
  const darkAccent = deriveAccentTone(dark.secondary);

  const css =
    `:root{--brand-primary:${primary};--brand-secondary:${secondary};--brand-on-primary:${onPrimary};` +
    `--brand-accent:${light.accent};--brand-bg:${light.bg};--brand-tint:${light.tint};--brand-ink:${light.ink};}` +
    `.dark{--brand-primary:${dark.primary};--brand-secondary:${dark.secondary};--brand-on-primary:${dark.onPrimary};` +
    `--brand-accent:${darkAccent};--brand-ink:${dark.secondary};}`;

  // Children de texto (NO dangerouslySetInnerHTML): los valores ya pasaron por
  // `isValidHexColor` línea arriba, así que no hay entrada sin sanear en el DOM.
  return <style id="brand-tokens">{css}</style>;
}
