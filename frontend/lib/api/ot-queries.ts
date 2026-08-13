// Cliente tipado de las consultas del organismo: el usuario arma su propia búsqueda sobre los
// trámites, la guarda y la exporta.
//
// El catálogo de campos NO está escrito aquí. Lo sirve el backend y esta capa solo lo transporta:
// es lo que hace que agregar un campo consultable sea tocar un archivo del servidor y verlo
// aparecer en el constructor sin desplegar frontend.
import { apiFetch } from "./client";

const base = "/api/v1/admin/ot/queries";
// El modelo de la consulta es COMPARTIDO con las consultas de la empresa gestora (`./queries`).
// Aquí se reexporta con los nombres con los que ya se cita en todo el módulo OT: son la misma
// forma, y tener dos definiciones haría que un enlace guardado en un lado dejara de abrir en el
// otro sin que nada avisara.
export type {
  QueryCondition as OtQueryCondition,
  QueryCoverageItem as OtQueryCoverageItem,
  QueryCoverageResult as OtQueryCoverageResult,
  QueryDateFilter as OtQueryDateFilter,
  QueryDefinition as OtQueryDefinition,
  QueryField as OtQueryField,
  QueryFieldKind as OtQueryFieldKind,
  QueryFieldOption as OtQueryFieldOption,
  QueryOperator as OtQueryOperator,
  QueryRangePreset as OtQueryRangePreset,
  SavedQuery as OtSavedQuery,
} from "./queries";

export { OPERATOR_LABEL, RANGE_PRESETS, UNARY_OPERATORS } from "./queries";

import type {
  QueryDefinition as OtQueryDefinition,
  QueryField as OtQueryField,
  QueryResult as SharedResult,
  SavedQuery as OtSavedQuery,
} from "./queries";
import { QUERY_MAX_PAGE_SIZE, QUERY_PAGE_SIZE } from "./queries";

/** Sobre qué fecha del trámite se aplica el rango. Esto sí es propio del organismo. */
export type OtQueryDateFieldId = "radicacion" | "decision" | "aprobacion" | "actualizacion";

export const DATE_FIELD_LABEL: Record<OtQueryDateFieldId, string> = {
  radicacion: "Fecha de radicación",
  decision: "Fecha de decisión",
  aprobacion: "Fecha de aprobación",
  actualizacion: "Última actualización",
};

export const OT_DATE_FIELDS: { value: string; label: string }[] = (
  Object.keys(DATE_FIELD_LABEL) as OtQueryDateFieldId[]
).map((value) => ({ value, label: DATE_FIELD_LABEL[value] }));

export interface OtQueryRow {
  procedureInstanceId: string;
  referenceNumber: string;
  placa: string | null;
  vin: string | null;
  clientTenantId: string;
  clientTenantName: string;
  modalidad: string;
  status: string;
  estadoOt: string;
  prioritario: boolean;
  subsanacionActiva: boolean;
  comprador: string | null;
  vendedor: string | null;
  tienePrenda: boolean;
  acreedorPrenda: string | null;
  tieneLicenciaTransito: boolean;
  transformaciones: string[];
  creadoEn: string;
  radicadoEn: string | null;
  ultimaRadicacionEn: string | null;
  decididoEn: string | null;
  aprobadoEn: string | null;
  actualizadoEn: string | null;
  decididoPor: string | null;
  horasHastaDecision: number | null;
  diasEnOrganismo: number | null;
  devoluciones: number;
  causalesUltimoRechazo: string[];
}

export type OtQueryResult = SharedResult<OtQueryRow>;

export const OT_QUERY_PAGE_SIZE = QUERY_PAGE_SIZE;
export const OT_QUERY_MAX_PAGE_SIZE = QUERY_MAX_PAGE_SIZE;

// ── Llamadas ────────────────────────────────────────────────────────────────────

function officeQuery(transitOfficeId?: string) {
  return transitOfficeId ? { transitOfficeId } : undefined;
}

export function fetchOtQueryFields(
  transitOfficeId?: string,
  signal?: AbortSignal,
): Promise<OtQueryField[]> {
  return apiFetch<OtQueryField[]>(`${base}/fields`, {
    query: officeQuery(transitOfficeId),
    signal,
  });
}

/**
 * La ejecución va por POST aunque sea una lectura: la definición lleva listas de placas pegadas
 * desde Excel, y meterlas en la barra de direcciones las expondría en los registros de los proxies
 * y chocaría con el límite de longitud de URL a las pocas decenas.
 */
export function runOtQuery(
  definition: OtQueryDefinition,
  options: { page?: number; pageSize?: number; transitOfficeId?: string; signal?: AbortSignal } = {},
): Promise<OtQueryResult> {
  return apiFetch<OtQueryResult>(`${base}/run`, {
    method: "POST",
    body: {
      definition,
      page: options.page ?? 1,
      pageSize: options.pageSize ?? OT_QUERY_PAGE_SIZE,
    },
    query: officeQuery(options.transitOfficeId),
    signal: options.signal,
  });
}

export function fetchOtSavedQueries(
  transitOfficeId?: string,
  signal?: AbortSignal,
): Promise<OtSavedQuery[]> {
  return apiFetch<OtSavedQuery[]>(`${base}/saved`, {
    query: officeQuery(transitOfficeId),
    signal,
  });
}

export function saveOtQuery(
  input: { id?: string; nombre: string; descripcion?: string | null; definition: OtQueryDefinition },
  transitOfficeId?: string,
): Promise<OtSavedQuery> {
  return apiFetch<OtSavedQuery>(`${base}/saved`, {
    method: "POST",
    body: input,
    query: officeQuery(transitOfficeId),
  });
}

export function deleteOtSavedQuery(id: string, transitOfficeId?: string): Promise<void> {
  return apiFetch<void>(`${base}/saved/${id}`, {
    method: "DELETE",
    query: officeQuery(transitOfficeId),
  });
}
