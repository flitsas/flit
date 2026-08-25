import type { ProcedureFamily } from "@/lib/api/types/procedure-parametrization";
import { toIsoDate, type DateRange } from "../_reportes/range";

export type TriState = "" | "true" | "false";

export interface DetailedReportFiltersState {
  range: DateRange;
  tenantId?: string;
  transitOfficeId?: string;
  procedureTypeId?: string;
  category?: string;
  status?: string;
  referenceNumber?: string;
  personDocument?: string;
  personName?: string;
  hasTransformation: TriState;
  isLeasing: TriState;
}

/**
 * Rango por defecto: los últimos 30 días. `reference` se inyecta en pruebas para no depender del
 * reloj del sistema.
 *
 * <p>Las fechas se arman con <c>toIsoDate</c>, que lee los componentes locales de la fecha, y no
 * con <c>toISOString()</c>, que devuelve UTC. Bogotá va cinco horas por detrás: con UTC, a partir
 * de las 7 de la tarde el «hasta» por defecto ya era el día siguiente y el «desde» se corría igual.
 * No fallaba nada — enseñaba un rango que parecía razonable y estaba movido un día—, que es peor.
 * Es además el ayudante que ya usa el resto del módulo de reportes, así que los dos coinciden.</p>
 */
export function defaultDetailedFilters(reference: Date = new Date()): DetailedReportFiltersState {
  const from = new Date(reference);
  from.setDate(from.getDate() - 30);
  return {
    range: { from: toIsoDate(from), to: toIsoDate(reference) },
    hasTransformation: "",
    isLeasing: "",
  };
}

// ── Categorías de trámite ────────────────────────────────────────────────────
// Los valores coinciden con la columna `category` de analytics.v_procedure_detail_report
// (derivada de procedure_types.family en el DDL HU #10814).
export const CATEGORY_LABELS: Record<string, string> = {
  matriculas: "Matrículas",
  traspasos: "Traspasos",
  otros: "Otros",
};

// Estados de negocio del trámite (TramiteEstado / ADR-0022), en orden de ciclo de vida.
// Coinciden con la columna `status` de la vista BI; las etiquetas legibles salen de statusLabel().
export const PROCEDURE_STATUSES = [
  "borrador",
  "preparado",
  "entregado",
  "aprobado",
  "rechazado",
  "anulado",
] as const;

/** Mapea la familia del tipo de trámite a la categoría que usa la vista BI. */
export function familyToCategory(family: ProcedureFamily): string {
  switch (family) {
    case "MATRICULAS":
      return "matriculas";
    case "TRASPASO":
      return "traspasos";
    default:
      return "otros";
  }
}

export function toQueryParams(
  filters: DetailedReportFiltersState,
  page = 1,
  pageSize = 20,
) {
  return {
    from: filters.range.from,
    to: filters.range.to,
    tenantId: filters.tenantId,
    transitOfficeId: filters.transitOfficeId || undefined,
    procedureTypeId: filters.procedureTypeId || undefined,
    category: filters.category || undefined,
    status: filters.status || undefined,
    referenceNumber: filters.referenceNumber || undefined,
    personDocument: filters.personDocument || undefined,
    personName: filters.personName || undefined,
    hasTransformation: filters.hasTransformation === "" ? undefined : filters.hasTransformation === "true",
    isLeasing: filters.isLeasing === "" ? undefined : filters.isLeasing === "true",
    page,
    pageSize,
  };
}
