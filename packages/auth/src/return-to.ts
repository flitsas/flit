// Validación de rutas de retorno sin dependencias de servidor: la usan las rutas /auth/* y también el navegador
// (p. ej. /auth/listo del frontend), por eso vive aparte de routes.ts.
/** Origen ficticio para resolver `returnTo` como lo haría el navegador y comprobar que no sale de la app. */
const RETURN_TO_ORIGIN = "http://app.local";

/**
 * Solo rutas relativas de esta misma app (evita un redireccionamiento abierto). Se rechaza cualquier caracter de
 * control (\x00-\x1f, \x7f) y la barra invertida: el navegador quita tabuladores y saltos de línea y trata `\` como
 * `/`, así que «/\t/evil.com» terminaría en «//evil.com». Además, resuelta contra un origen ficticio, la ruta debe
 * quedarse en ese origen.
 */
export function safeReturnTo(value: string | null | undefined): string {
  if (!value || !value.startsWith("/") || value.startsWith("//") || /[\x00-\x1f\x7f\\]/.test(value)) return "/";
  try {
    const resolved = new URL(value, RETURN_TO_ORIGIN);
    return resolved.origin === RETURN_TO_ORIGIN && resolved.pathname.startsWith("/") ? value : "/";
  } catch {
    return "/";
  }
}
