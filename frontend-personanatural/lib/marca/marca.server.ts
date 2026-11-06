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

export async function obtenerMarcaPunto(codigo: string, opciones: OpcionesMarca): Promise<MarcaAplicable | null> {
  if (!codigo || codigo.length > 64) return null;
  const cabeceras = new Headers({ accept: "application/json" });
  if (opciones.ipCliente) cabeceras.set("x-forwarded-for", opciones.ipCliente);
  try {
    const respuesta = await (opciones.fetchImpl ?? fetch)(`${opciones.apiOrigin}${PREFIJO_BFF}/punto/marca/${encodeURIComponent(codigo)}`, {
      method: "GET",
      headers: cabeceras,
      cache: "no-store",
      redirect: "manual",
      signal: AbortSignal.timeout(ESPERA_MS),
    });
    if (!respuesta.ok) return null;
    return marcaAplicable(await respuesta.json());
  } catch {
    return null;
  }
}
