import type { ProcedureFamily, ProcedureTypeSummary } from "@/lib/api/types/procedure-parametrization";
import type { DateRange } from "../_reportes/range";

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

export function defaultDetailedFilters(): DetailedReportFiltersState {
  const to = new Date();
  const from = new Date(to);
  from.setDate(from.getDate() - 30);
  return {
    range: { from: from.toISOString().slice(0, 10), to: to.toISOString().slice(0, 10) },
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
  vehicular: "Vehicular",
  otros: "Otros",
};

const CATEGORY_ORDER = ["matriculas", "traspasos", "vehicular", "otros"];

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

export interface ProcedureTypeGroup {
  category: string;
  label: string;
  types: ProcedureTypeSummary[];
}

/** Agrupa los tipos de trámite por categoría, ordenados para el selector unificado. */
export function groupProcedureTypes(types: ProcedureTypeSummary[]): ProcedureTypeGroup[] {
  const byCategory = new Map<string, ProcedureTypeSummary[]>();
  for (const type of types) {
    const category = familyToCategory(type.family);
    const bucket = byCategory.get(category);
    if (bucket) bucket.push(type);
    else byCategory.set(category, [type]);
  }
  return CATEGORY_ORDER.filter((category) => byCategory.has(category)).map((category) => ({
    category,
    label: CATEGORY_LABELS[category] ?? category,
    types: (byCategory.get(category) ?? []).sort((a, b) => a.name.localeCompare(b.name)),
  }));
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
