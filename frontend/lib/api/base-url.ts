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
  return isFlitHost(window.location.host) ? apiBaseForPageRoot(configuredBase) : "";
}

/**
 * La base horneada en build con la raíz de la página (dominio alternativo de PDN): en `app.flitsas.com` la API es
 * `api.flitsas.com`, no la `api.flitsas.online` horneada; en `flitsas.online` (y en DEV/QA, donde las raíces ya
 * coinciden) no cambia nada. Solo cambia la raíz (los dos últimos nombres): el resto del host, el puerto y el path se
 * conservan, así `api.dev.flitsas.online` nunca puede volverse la API de otro ambiente. Solo en un host FLIT
 * (`NEXT_PUBLIC_FLIT_HOSTS`); en SSR, sin base absoluta o en un host de red, la base tal cual.
 */
export function apiBaseForPageRoot(configuredBase: string): string {
  if (typeof window === "undefined" || !configuredBase || !isFlitHost(window.location.host)) {
    return configuredBase;
  }
  let api: URL;
  try {
    api = new URL(configuredBase);
  } catch {
    return configuredBase;
  }
  const apiRoot = rootOf(api.hostname);
  const pageRoot = rootOf(window.location.hostname);
  if (!apiRoot || !pageRoot || apiRoot === pageRoot || !isFlitHost(api.host)) return configuredBase;
  const aligned = new URL(api.toString());
  aligned.hostname = api.hostname.slice(0, api.hostname.length - apiRoot.length) + pageRoot;
  // Solo entre raíces FLIT (api.flitsas.online ↔ api.flitsas.com): una API ajena nunca se reescribe.
  if (!isFlitHost(aligned.host)) return configuredBase;
  return aligned.toString().replace(/\/+$/, "");
}

/** Los dos últimos nombres del host (`flitsas.online`, `flitsas.com`); vacío para IPs y hosts de una sola etiqueta. */
function rootOf(hostname: string): string {
  const labels = hostname.toLowerCase().replace(/\.$/, "").split(".");
  if (labels.length < 2 || labels.every((l) => /^\d+$/.test(l))) return "";
  return labels.slice(-2).join(".");
}
