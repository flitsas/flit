// Clasificación de host FLIT vs. host de red (HU #12419, ADR-0060 §D5). Determina, sin llamar al
// backend, si el dominio de la petición es uno de los propios de FLIT (en cuyo caso NUNCA se
// resuelve marca: cero llamadas nuevas, AC7) o un dominio de red (que sí exige resolver la marca
// en el servidor, AC1).
//
// Uso de ejemplo:
//   isFlitHost("localhost:3000")            // → true
//   isFlitHost("dev.flitsas.online")        // → true (matchea "*.flitsas.online")
//   isFlitHost("app.movilidadandina.com")   // → false
//
// La lista admite excepciones con prefijo "!" (coincidencia EXACTA, sin comodín), que se evalúan
// ANTES que los patrones positivos. Con
// `NEXT_PUBLIC_FLIT_HOSTS="*.flitsas.online,!marcablancadev.flitsas.online"` (HU #12761):
//   isFlitHost("marcablancadev.flitsas.online")      // → false (negado, aunque matchee el comodín)
//   isFlitHost("sub.marcablancadev.flitsas.online")  // → true  (la negación no cubre subdominios)

/**
 * Lista por defecto cuando `NEXT_PUBLIC_FLIT_HOSTS` no está definida (dev local y respaldo).
 * `*.dominio` matchea el dominio raíz y cualquier subdominio (paridad con `ReservedHosts` .NET).
 * En PDN el frontend se sirve en la raíz `flitsas.online`; tratarla como dominio de red rompía el
 * login (arreglo 5ae9578f, traído de `release`).
 * `!host` excluye ese host exacto aunque otro patrón lo cubra.
 *
 * Las negaciones de los hosts de prueba de marca blanca viven TAMBIÉN aquí, no solo en el
 * build-arg de `.github/workflows/cd.yml` (HU #12761): tratarlos como dominio de red es una
 * decisión de seguridad y no puede depender de que la variable llegue al build. Si
 * `NEXT_PUBLIC_FLIT_HOSTS` faltara o llegara vacía (build local, `docker build` sin el arg,
 * una ruta de build por compose, un typo en el workflow), el respaldo volvería a clasificarlos
 * como FLIT en silencio, sin que nada fallara ni avisara. Con las negaciones horneadas aquí,
 * el respaldo es simétrico al `appsettings.json` del backend, que sí viaja con la imagen.
 * Mantener sincronizadas estas tres entradas con `cd.yml` y `frontend/.env.example`.
 */
const DEFAULT_FLIT_HOSTS = [
  "localhost",
  "127.0.0.1",
  "*.flitsas.online",
  "*.flitsas.com",
  "!marcablancadev.flitsas.online",
  "!marcablancaqa.flitsas.online",
  "!marcablancapdn.flitsas.online",
];

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
    // Alineado con ReservedHosts (.NET): `*.flitsas.online` también reserva el apex.
    const baseDomain = pattern.slice(2); // "flitsas.online"
    const suffix = pattern.slice(1); // ".flitsas.online"
    return host === baseDomain || (host.length > suffix.length && host.endsWith(suffix));
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

  // Las exclusiones (`!host`) ganan siempre y cortan la evaluación: un host de prueba de marca
  // blanca bajo un dominio propio (HU #12761) debe tratarse como dominio de red.
  const isExcluded = patterns.some(
    (pattern) => pattern.startsWith("!") && host === pattern.slice(1),
  );
  if (isExcluded) return false;

  return patterns.some((pattern) => !pattern.startsWith("!") && matchesPattern(host, pattern));
}
