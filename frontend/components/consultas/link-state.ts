import type { QueryDefinition } from "@/lib/api/queries";

/**
 * La consulta dentro del enlace.
 *
 * Va en base64 y no como parámetros sueltos porque una lista de cuarenta placas convertiría la
 * dirección en algo que ningún chat respeta al pegarlo. Se codifica a UTF-8 antes para que los
 * nombres con tilde no rompan `btoa`.
 */
export function encodeDefinition(definition: QueryDefinition): string {
  const json = JSON.stringify(definition);
  const bytes = new TextEncoder().encode(json);
  return btoa(String.fromCharCode(...bytes));
}

export function decodeDefinition(encoded: string): QueryDefinition | null {
  try {
    const binary = atob(encoded);
    const bytes = Uint8Array.from(binary, (c) => c.charCodeAt(0));
    const parsed: unknown = JSON.parse(new TextDecoder().decode(bytes));
    if (!parsed || typeof parsed !== "object" || !("fechas" in parsed)) return null;
    return parsed as QueryDefinition;
  } catch {
    // Un enlace recortado por el chat no debe dejar la pantalla en blanco: se abre la consulta
    // vacía, que es un sitio del que el usuario sabe salir.
    return null;
  }
}
