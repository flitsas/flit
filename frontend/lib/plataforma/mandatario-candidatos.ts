/**
 * HU #13147 (ADR-0066) — candidatos del 409 `mandatario_requerido` de la aprobación del OT.
 *
 * El backend calcula los mandatarios VÁLIDOS (activos, vigentes y con firma utilizable, dentro del
 * scope del organismo): el cliente solo los muestra. No filtra por `isActive` ni `companyTenantIds`
 * ni consulta la lista completa del organismo. Nunca traen documento ni ruta de firma (Ley 1581).
 */
import { FORMAS_DE_FIRMA } from "@/lib/plataforma/mandatario-modelo";

export interface MandatarioCandidato {
  id: string;
  nombre: string;
  formaFirma: "baul" | "biometria" | null;
}

/**
 * Lee `candidatos` del cuerpo del 409. Devuelve `null` si el cuerpo no trae la lista (respuesta
 * mal formada: no se puede mostrar un diálogo honesto) y `[]` si el backend dice que no hay ninguno.
 */
export function candidatosDeRespuesta(body: unknown): MandatarioCandidato[] | null {
  if (!body || typeof body !== "object") return null;
  const lista = (body as { candidatos?: unknown }).candidatos;
  if (!Array.isArray(lista)) return null;
  const candidatos: MandatarioCandidato[] = [];
  for (const item of lista) {
    if (!item || typeof item !== "object") continue;
    const { id, nombre, formaFirma } = item as Record<string, unknown>;
    if (typeof id !== "string" || !id || typeof nombre !== "string") continue;
    candidatos.push({
      id,
      nombre,
      formaFirma: formaFirma === "baul" || formaFirma === "biometria" ? formaFirma : null,
    });
  }
  return candidatos;
}

/** Rótulo de la forma de firma, el mismo de las listas de mandatarios; vacío si el backend no la trae. */
export function formaFirmaLabel(forma: MandatarioCandidato["formaFirma"]): string {
  return FORMAS_DE_FIRMA.find((f) => f.value === forma)?.label ?? "";
}
