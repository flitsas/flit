// Productos del catálogo que todavía no tienen app desplegada (Suite:Hosts:ComingSoon en la API): su tarjeta lleva a
// /proximamente/<código> en vez de a un host que no responde. Función pura.

export interface ComingSoonProduct {
  code: string;
  name: string;
  description: string;
  /** Nombre del ícono de platform.products (lo traduce `appIcon` de @flit/shell). */
  icon: string;
}

const PRODUCTS: Record<string, ComingSoonProduct> = {
  comparendos: { code: "comparendos", name: "Comparendos", description: "Gestión de comparendos de tránsito.", icon: "ticket" },
  diagnostico: { code: "diagnostico", name: "Diagnóstico", description: "Diagnóstico de flotas y vehículos.", icon: "gauge" },
};

export function comingSoonProduct(code: string): ComingSoonProduct | null {
  return Object.hasOwn(PRODUCTS, code) ? PRODUCTS[code] : null;
}
