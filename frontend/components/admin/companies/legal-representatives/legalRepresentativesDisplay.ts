// Helpers de presentación del directorio de representantes legales (HU #10904): nombre completo,
// enmascarado de documento (PII, Ley 1581), estado de firma/identidad y etiquetas de tipo de trámite.
import type { StatusTone } from "@/components/atom/StatusBadge";
import { findProcedureType } from "@/lib/constants/procedure-types";
import type { LegalRepresentativeItem } from "@/lib/api/admin-legal-representatives";

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

/** Nombres de los tipos de trámite marcados (según el catálogo estático alineado al backend). */
export function procedureTypeLabels(ids: readonly string[]): string[] {
  return ids.map((id) => findProcedureType(id)?.name ?? "Trámite").filter(Boolean);
}
