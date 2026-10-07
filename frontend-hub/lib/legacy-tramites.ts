// Reparto de las rutas que hoy sirve Trámites en la raíz de cada ambiente (A-11, HU #13002; plan maestro §4.3). Con
// el hub en la raíz, lo que es de la plataforma se queda aquí y lo de Trámites se redirige con 308 a su host,
// conservando ruta y parámetros. Función pura: la usa el middleware y la prueban sus pruebas.

/** Rutas que atiende el hub. Todo lo demás era de Trámites. */
const HUB_PREFIXES = [
  "/login",
  "/auth/",
  "/reset-password",
  "/invite/",
  "/403",
  "/connect/",
  "/.well-known/",
  "/api/",
  "/healthz",
  // Tarjetas de los productos que aún no existen (me/apps las enlaza aquí): sin esto iban a Trámites y daban 404.
  "/proximamente/",
  "/email-assets/",
  "/_next/",
  "/assets/",
];
const HUB_FILES = new Set(["/icon.svg", "/favicon.ico", "/robots.txt"]);

/**
 * URL de Trámites a la que redirigir, o `null` si la atiende el hub.
 *
 * Mientras el hub no tenga su perfil ni su administración (B-12), `/profile/*` y todo `/admin/*` siguen en Trámites.
 */
export function legacyTramitesRedirect(url: URL, tramitesUrl: string): string | null {
  const { pathname, search } = url;

  if (pathname === "/") {
    // `/?m=<módulo>` era la navegación de Trámites; la raíz sola, o el dashboard, es el inicio del hub.
    const tramitesModule = url.searchParams.get("m");
    return tramitesModule && tramitesModule !== "dashboard" ? target(tramitesUrl, pathname, search) : null;
  }

  if (HUB_FILES.has(pathname) || HUB_PREFIXES.some((prefix) => pathname === prefix.replace(/\/$/, "") || pathname.startsWith(prefix))) {
    return null;
  }

  return target(tramitesUrl, pathname, search);
}

function target(tramitesUrl: string, pathname: string, search: string): string {
  return `${tramitesUrl.replace(/\/+$/, "")}${pathname}${search}`;
}
