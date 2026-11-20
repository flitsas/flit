"use client";

// Logotipo de la marca activa (HU #12419 AC2, ADR-0060 §D5). En host FLIT usa EXACTAMENTE el
// mismo asset/props de hoy (AC7 paridad); en un host de red usa `brand.logoUrl` (servido por
// `/api/v1/public/branding/logos/{id}`, HU #12418) para ambas variantes — la marca no distingue
// "logo claro/oscuro": un solo logo.
//
// Uso de ejemplo:
//   <BrandLogo variant="white" className="h-10 w-auto" />                // vía contexto (Shell/Login)
//   <BrandLogo brand={brand} variant="white" className="h-12 w-auto" />  // Server Component (activate)
import { useBrand } from "./BrandProvider";
import type { Brand } from "./types";

const FLIT_ASSETS = {
  white: "/assets/logo-flit-white.svg",
  dark: "/assets/logo-flit-dark.svg",
} as const;

/**
 * `white`/`dark` fijan el logo; `auto` (UI kit «Diseño Inteligente», HU #14478) pinta el de tinta en tema claro y el
 * blanco en oscuro. Con un logo de Marca Blanca siempre es uno solo.
 */
export type BrandLogoVariant = keyof typeof FLIT_ASSETS | "auto";

export function BrandLogo({
  brand,
  variant,
  className,
  alt,
}: {
  /** Override explícito — úsalo en Server Components que ya resolvieron la marca (evita un
   * segundo camino de lectura; `useBrand()` sigue llamándose para respetar las reglas de hooks,
   * pero su valor se ignora cuando se pasa `brand`). */
  brand?: Brand;
  variant: BrandLogoVariant;
  className?: string;
  alt?: string;
}) {
  const contextBrand = useBrand();
  const active = brand ?? contextBrand;
  const label = alt ?? active.platformName;

  if (active.logoUrl) return <img src={active.logoUrl} alt={label} className={className} />;
  if (variant !== "auto") return <img src={FLIT_ASSETS[variant]} alt={label} className={className} />;

  // Las dos imágenes alternan con la clase `dark`; la oculta queda en display:none y no entra al árbol de accesibilidad.
  const cls = className ?? "";
  return (
    <>
      <img src={FLIT_ASSETS.dark} alt={label} className={`${cls} dark:hidden!`} />
      <img src={FLIT_ASSETS.white} alt={label} className={`${cls} hidden! dark:block!`} />
    </>
  );
}
