// Clasificación de host FLIT vs. host de red (HU #12419, ADR-0060 §D5). Determina, sin llamar al
// backend, si el dominio de la petición es uno de los propios de FLIT (en cuyo caso NUNCA se
// resuelve marca: cero llamadas nuevas, AC7) o un dominio de red (que sí exige resolver la marca
// en el servidor, AC1).
//
// Uso de ejemplo:
//   isFlitHost("localhost:3000")            // → true
//   isFlitHost("dev.flitsas.online")        // → true (matchea "*.flitsas.online")
//   isFlitHost("app.movilidadandina.com")   // → false

/**
 * Lista por defecto cuando `NEXT_PUBLIC_FLIT_HOSTS` no está definida (dev local y respaldo).
 * `*.dominio` matchea cualquier subdominio de `dominio` (no el dominio raíz sin subdominio).
 */
const DEFAULT_FLIT_HOSTS = ["localhost", "127.0.0.1", "*.flitsas.online", "*.flitsas.com"];

function parseHostList(raw: string | undefined): string[] {
  if (!raw || !raw.trim()) return DEFAULT_FLIT_HOSTS;
  const items = raw
    .split(",")
    .map((h) => h.trim().toLowerCase())
    .filter(Boolean);
  return items.length > 0 ? items : DEFAULT_FLIT_HOSTS;
}

/** Quita el puerto de un `host` header (`localhost:3000` → `localhost`), IPv6-safe. */
function stripPort(host: string): string {
  if (host.startsWith("[")) {
    const closing = host.indexOf("]");
    return closing >= 0 ? host.slice(0, closing + 1) : host;
  }
  const idx = host.lastIndexOf(":");
  return idx > 0 ? host.slice(0, idx) : host;
}

function matchesPattern(host: string, pattern: string): boolean {
  if (pattern.startsWith("*.")) {
    const suffix = pattern.slice(1); // ".dominio.tld"
    return host.length > suffix.length && host.endsWith(suffix);
  }
  return host === pattern;
}

/**
 * `true` si `hostHeader` pertenece a FLIT (según `NEXT_PUBLIC_FLIT_HOSTS` o el respaldo por
 * defecto). Sin host conocido (SSR sin cabecera, tests) se asume FLIT — es el comportamiento de
 * hoy y el lado seguro del respaldo (AC5).
 */
export function isFlitHost(hostHeader: string | null | undefined): boolean {
  if (!hostHeader || !hostHeader.trim()) return true;
  const host = stripPort(hostHeader.trim().toLowerCase());
  const patterns = parseHostList(process.env.NEXT_PUBLIC_FLIT_HOSTS);
  return patterns.some((pattern) => matchesPattern(host, pattern));
}
