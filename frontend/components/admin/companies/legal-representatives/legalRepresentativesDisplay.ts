// Helpers de presentación del directorio de representantes legales (HU #10904): nombre completo,
// enmascarado de documento (PII, Ley 1581), estado de firma/identidad y etiquetas de tipo de trámite.
import type { StatusTone } from "@/components/atom/StatusBadge";
import type { AssignableProcedureType, LegalRepresentativeItem } from "@/lib/api/admin-legal-representatives";

/** Nombre completo del representante: nombres + primer apellido + segundo apellido. */
export function fullName(rep: Pick<LegalRepresentativeItem, "name" | "firstLastName" | "secondLastName">): string {
  return [rep.name, rep.firstLastName, rep.secondLastName]
    .map((p) => p?.trim())
    .filter((p): p is string => Boolean(p))
    .join(" ");
}

/** Enmascara el número de documento (PII): solo los últimos 4 caracteres. */
export function maskDocument(documentNumber: string): string {
  if (documentNumber.length <= 4) return "••••";
  return `••••${documentNumber.slice(-4)}`;
}

/** Estado de firma/identidad para el StatusBadge (tono semántico + etiqueta). */
export function signatureStatus(hasSignatureOrIdentity: boolean): { tone: StatusTone; label: string } {
  return hasSignatureOrIdentity
    ? { tone: "success", label: "Con firma o identidad" }
    : { tone: "warning", label: "Sin firma ni identidad" };
}

/**
 * Nombres de los tipos de trámite marcados, resueltos contra el catálogo real (activos + publicados)
 * cargado desde el backend. Si un id ya no está en el catálogo (p. ej. tipo archivado tras marcarlo),
 * cae a su código o a "Trámite" para no romper la fila.
 */
export function procedureTypeLabels(
  ids: readonly string[],
  catalog: readonly AssignableProcedureType[],
): string[] {
  return ids.map((id) => {
    const match = catalog.find((p) => p.id === id);
    return match?.name ?? match?.code ?? "Trámite";
  });
}
