// Tipos e identidad de marca (HU #12419, ADR-0060 §D5). El contrato de red
// (`GET /api/v1/public/branding` / `GET /api/v1/me/branding`,
// `contracts/openapi/core-api.v1.yaml`, `BrandIdentityResponse`) siempre responde 200 con esta
// misma forma — para FLIT o para una red publicada — así que el frontend nunca distingue
// "sin marca" de "marca FLIT": ambas son la MISMA constante `FLIT_BRAND`.
//
// Uso de ejemplo:
//   const brand: Brand = await resolveBrand();
//   if (isFlitBrand(brand)) { ...camino de hoy, sin cambios... }

export interface BrandColors {
  primary: string;
  secondary: string;
  onPrimary: string;
}

export interface Brand {
  platformName: string;
  /** Relativa al Gateway (`/api/v1/public/branding/logos/{id}`) o `null` si es FLIT. */
  logoUrl: string | null;
  colors: BrandColors;
  version: number;
}

/**
 * Identidad FLIT — IDÉNTICA a `BrandIdentity.Flit` del backend (contratos-api.md §1). `version: 0`
 * es la señal canónica de "no hay marca de red": el backend NUNCA emite `version: 0` para una
 * marca publicada (empieza en 1 en el primer `publish`).
 */
export const FLIT_BRAND: Brand = {
  platformName: "FLIT 2.0",
  logoUrl: null,
  colors: { primary: "#162744", secondary: "#557EFF", onPrimary: "#FFFFFF" },
  version: 0,
};

/** `true` si `brand` es la identidad FLIT (por contenido, no por referencia — ver cabecera). */
export function isFlitBrand(brand: Brand): boolean {
  return brand.version === 0;
}

/**
 * Nombre a mostrar: el de la red si hay marca, o `flitLabel` (por defecto el de FLIT_BRAND) si
 * no. Existe porque algunas superficies históricas usan una variante corta ("FLIT" en vez de
 * "FLIT 2.0") que se preserva byte a byte en host FLIT (AC7 #12419).
 */
export function brandDisplayName(brand: Brand, flitLabel: string = FLIT_BRAND.platformName): string {
  return isFlitBrand(brand) ? flitLabel : brand.platformName;
}
