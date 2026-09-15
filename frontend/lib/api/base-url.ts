// Base de la API HTTP según el host actual (HU #12419, ADR-0060 §D5). En un dominio FLIT usa la
// base cross-origin horneada en build (`NEXT_PUBLIC_API_BASE_URL`, `api.<env>.flitsas.online`);
// en un dominio de red el borde ya enruta `/api/v1/*` al Gateway en el MISMO origen (#12421), así
// que basta con devolver "" (cadena vacía) para que `resolveApiUrl`/`apiUrl` caigan a
// `window.location.origin` — el mismo patrón que ya usan cuando `NEXT_PUBLIC_API_BASE_URL` no
// está definida (dev local).
//
// Uso de ejemplo:
//   resolveApiBase(API_BASE_URL) // "" en dev FLIT / host de red · "https://api.dev...:.../api/v1" en prod FLIT
import { isFlitHost } from "@/lib/brand/hosts";

/**
 * `configuredBase` es la base ya resuelta del módulo llamante (p. ej. `API_BASE_URL` de
 * `lib/api/client.ts`) — se pasa explícita en vez de leer `process.env` aquí para no duplicar la
 * lista de variables aceptadas (`NEXT_PUBLIC_API_BASE_URL` vs. `NEXT_PUBLIC_API_URL` en
 * `tramites-client.ts`).
 */
export function resolveApiBase(configuredBase: string): string {
  if (typeof window === "undefined") {
    // SSR/Server Components: no hay `window.location`, se respeta la base configurada tal cual
    // (estos clientes se usan sobre todo desde Client Components; este camino es defensivo).
    return configuredBase;
  }
  return isFlitHost(window.location.host) ? configuredBase : "";
}
