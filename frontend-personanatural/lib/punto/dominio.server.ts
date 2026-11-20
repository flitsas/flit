// Épica #14297, HU #14386 — Resuelve EN EL SERVIDOR, en cada petición, a qué organismo pertenece el host con que se abrió
// el portal. Sin caché de ningún tipo: un dominio recién activado o retirado se nota en la siguiente carga. Cualquier
// falla devuelve `null` y la web de persona queda como hoy.
import "server-only";
import { PREFIJO_BFF } from "@/lib/server/proxy";

const ESPERA_MS = 3000;
const ETIQUETA = /^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$/;

export interface OpcionesDominio {
  apiOrigin: string;
  ipCliente?: string | undefined;
  fetchImpl?: typeof fetch;
}

/**
 * Host normalizado (minúsculas, sin punto final) o `null` si no es un nombre de dominio plausible. Un host con puerto,
 * una IP, `localhost` o algo con caracteres raros no es el dominio de un organismo y no se envía al BFF.
 */
export function hostValido(crudo: string | null | undefined): string | null {
  const host = (crudo ?? "").trim().toLowerCase().replace(/\.$/, "");
  if (!host || host.length > 253) return null;
  const etiquetas = host.split(".");
  if (etiquetas.length < 2 || !etiquetas.every((e) => ETIQUETA.test(e))) return null;
  if (/^\d+$/.test(etiquetas[etiquetas.length - 1]!)) return null;
  return host;
}

/** Código del organismo dueño del dominio activo, o `null` si el host no resuelve. */
export async function resolverDominioPortal(
  hostCrudo: string | null | undefined,
  opciones: OpcionesDominio,
): Promise<string | null> {
  const host = hostValido(hostCrudo);
  if (!host) return null;
  const cabeceras = new Headers({ accept: "application/json" });
  if (opciones.ipCliente) cabeceras.set("x-forwarded-for", opciones.ipCliente);
  try {
    const respuesta = await (opciones.fetchImpl ?? fetch)(
      `${opciones.apiOrigin}${PREFIJO_BFF}/punto/dominio/${encodeURIComponent(host)}`,
      {
        method: "GET",
        headers: cabeceras,
        cache: "no-store",
        redirect: "manual",
        signal: AbortSignal.timeout(ESPERA_MS),
      },
    );
    if (!respuesta.ok) return null;
    const cuerpo = (await respuesta.json()) as { codigo?: unknown } | null;
    const codigo = typeof cuerpo?.codigo === "string" ? cuerpo.codigo.trim() : "";
    return codigo && codigo.length <= 64 ? codigo : null;
  } catch {
    return null;
  }
}
