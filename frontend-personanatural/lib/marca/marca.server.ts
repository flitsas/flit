// HU #14379 — Lectura de la marca del punto EN EL SERVIDOR, en cada petición. Sin caché de ningún tipo: lo publicado o
// retirado se ve en la siguiente carga. Cualquier falla devuelve `null` (el portal se ve como hoy).
import "server-only";
import { marcaAplicable, type MarcaAplicable } from "@/lib/marca/marca";
import { PREFIJO_BFF } from "@/lib/server/proxy";

const ESPERA_MS = 3000;

export interface OpcionesMarca {
  apiOrigin: string;
  ipCliente?: string | undefined;
  fetchImpl?: typeof fetch;
}

async function leerMarca(ruta: string, opciones: OpcionesMarca, origenes: readonly string[]): Promise<MarcaAplicable | null> {
  const cabeceras = new Headers({ accept: "application/json" });
  if (opciones.ipCliente) cabeceras.set("x-forwarded-for", opciones.ipCliente);
  try {
    const respuesta = await (opciones.fetchImpl ?? fetch)(`${opciones.apiOrigin}${PREFIJO_BFF}${ruta}`, {
      method: "GET",
      headers: cabeceras,
      cache: "no-store",
      redirect: "manual",
      signal: AbortSignal.timeout(ESPERA_MS),
    });
    if (!respuesta.ok) return null;
    return marcaAplicable(await respuesta.json(), origenes);
  } catch {
    return null;
  }
}

export async function obtenerMarcaPunto(codigo: string, opciones: OpcionesMarca): Promise<MarcaAplicable | null> {
  if (!codigo || codigo.length > 64) return null;
  return leerMarca(`/punto/marca/${encodeURIComponent(codigo)}`, opciones, ["organismo"]);
}

/**
 * HU #14403 (Feature #14400) — Marca de la web de persona (`GET /marca/web`), con las mismas garantías: sin caché, sin
 * lanzar errores y `null` ante cualquier falla o si no hay marca publicada.
 */
export async function obtenerMarcaWeb(opciones: OpcionesMarca): Promise<MarcaAplicable | null> {
  return leerMarca("/marca/web", opciones, ["organismo", "web"]);
}
