"use client";

// Contexto de marca en cliente (HU #12419, ADR-0060 §D5). Se monta UNA sola vez en
// `app/layout.tsx` con la marca YA resuelta en el servidor — nunca hace fetch en el navegador.
// `Shell.tsx`, `Login.tsx`, `AuthCard.tsx` y `BrandLogo` leen el logo/nombre de aquí.
//
// Uso de ejemplo:
//   <BrandProvider brand={brand}><Shell/></BrandProvider>
//   const { platformName, logoUrl, colors, isFlit } = useBrand();
import { createContext, useContext, type ReactNode } from "react";
import { FLIT_BRAND, isFlitBrand, type Brand } from "@/lib/brand/types";

const BrandContext = createContext<Brand>(FLIT_BRAND);

export function BrandProvider({ brand, children }: { brand: Brand; children: ReactNode }) {
  return <BrandContext.Provider value={brand}>{children}</BrandContext.Provider>;
}

export interface UseBrandResult extends Brand {
  /** `true` si no hay marca de red activa (identidad FLIT). */
  isFlit: boolean;
}

/** Fuera de `<BrandProvider>` devuelve `FLIT_BRAND` (valor por defecto del contexto) — nunca lanza. */
export function useBrand(): UseBrandResult {
  const brand = useContext(BrandContext);
  return { ...brand, isFlit: isFlitBrand(brand) };
}
