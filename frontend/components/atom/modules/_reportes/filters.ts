// Estado de los filtros globales de Reportes 2.0 (HU-C): compartidos por las
// 5 pestañas y persistentes al cambiar de pestaña.
import type { CompareMode, MetricsParams } from "@/lib/api/analytics-v2";
import { defaultRange, type DateRange } from "./range";

/** Filtros globales elevados sobre las pestañas. Cadena vacía = sin filtro. */
export interface ReportFilters {
  range: DateRange;
  /** Compañía elegida por SuperAdmin; '' = sin elegir (tenant propio para no-super). */
  tenantId: string;
  transitOfficeId: string;
  procedureTypeId: string;
  operatorUserId: string;
  compareWith: "" | CompareMode;
}

/** Filtros iniciales: mes en curso, sin comparación ni filtros adicionales. */
export function defaultFilters(reference?: Date): ReportFilters {
  return {
    range: defaultRange(reference),
    tenantId: "",
    transitOfficeId: "",
    procedureTypeId: "",
    operatorUserId: "",
    compareWith: "",
  };
}

/** Convierte los filtros de UI en los query params del contrato (§4.1). */
export function toMetricsParams(filters: ReportFilters, extra?: Partial<MetricsParams>): MetricsParams {
  return {
    from: filters.range.from,
    to: filters.range.to,
    tenantId: filters.tenantId || undefined,
    transitOfficeId: filters.transitOfficeId.trim() || undefined,
    procedureTypeId: filters.procedureTypeId.trim() || undefined,
    operatorUserId: filters.operatorUserId.trim() || undefined,
    compareWith: filters.compareWith || undefined,
    ...extra,
  };
}

/**
 * Ventana comparada calculada en cliente para los endpoints LEGADOS (overview,
 * monthly-trend), que no soportan `compareWith`. Espeja la semántica del §4.1:
 * `previous_period` = misma duración inmediatamente anterior; `previous_year` =
 * mismas fechas un año atrás.
 */
export function compareRange(range: DateRange, mode: CompareMode): DateRange {
  const from = new Date(`${range.from}T00:00:00`);
  const to = new Date(`${range.to}T00:00:00`);
  if (mode === "previous_year") {
    from.setFullYear(from.getFullYear() - 1);
    to.setFullYear(to.getFullYear() - 1);
    return { from: toIso(from), to: toIso(to) };
  }
  const days = Math.round((to.getTime() - from.getTime()) / 86_400_000) + 1;
  const prevTo = new Date(from);
  prevTo.setDate(prevTo.getDate() - 1);
  const prevFrom = new Date(prevTo);
  prevFrom.setDate(prevFrom.getDate() - days + 1);
  return { from: toIso(prevFrom), to: toIso(prevTo) };
}

function toIso(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, "0");
  const d = String(date.getDate()).padStart(2, "0");
  return `${y}-${m}-${d}`;
}
