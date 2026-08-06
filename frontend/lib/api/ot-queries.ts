// Cliente tipado de las consultas del organismo: el usuario arma su propia búsqueda sobre los
// trámites, la guarda y la exporta.
//
// El catálogo de campos NO está escrito aquí. Lo sirve el backend y esta capa solo lo transporta:
// es lo que hace que agregar un campo consultable sea tocar un archivo del servidor y verlo
// aparecer en el constructor sin desplegar frontend.
import { apiFetch } from "./client";

const base = "/api/v1/admin/ot/queries";

// ── Catálogo de campos ──────────────────────────────────────────────────────────

export type OtQueryFieldKind = "texto" | "opcion" | "booleano";

export type OtQueryOperator =
  | "es_alguno"
  | "no_es_ninguno"
  | "contiene"
  | "esta_vacio"
  | "no_esta_vacio";

export interface OtQueryFieldOption {
  value: string;
  label: string;
}

export interface OtQueryField {
  id: string;
  label: string;
  kind: OtQueryFieldKind;
  group: string;
  operators: OtQueryOperator[];
  options: OtQueryFieldOption[];
  hint: string | null;
  /** Si tiene sentido pegar una lista de valores (placas, VIN, radicados). */
  admiteLista: boolean;
}

/** Etiquetas de los operadores tal y como se leen dentro de un chip. */
export const OPERATOR_LABEL: Record<OtQueryOperator, string> = {
  es_alguno: "es",
  no_es_ninguno: "no es",
  contiene: "contiene",
  esta_vacio: "está vacío",
  no_esta_vacio: "tiene dato",
};

export const UNARY_OPERATORS: OtQueryOperator[] = ["esta_vacio", "no_esta_vacio"];

// ── Definición ──────────────────────────────────────────────────────────────────

export interface OtQueryCondition {
  fieldId: string;
  operator: OtQueryOperator;
  values: string[];
}

export type OtQueryDateFieldId = "radicacion" | "decision" | "actualizacion";

export type OtQueryRangePreset =
  | "hoy"
  | "ultimos_7"
  | "ultimos_30"
  | "ultimos_90"
  | "mes_actual"
  | "mes_anterior"
  | "anio_actual"
  | "personalizado";

/**
 * El rango se guarda RELATIVO («últimos 30 días»), no con extremos fijos, y lo resuelve el servidor
 * contra el día de Bogotá en cada ejecución. Una consulta guardada con «1 al 31 de agosto» mentiría
 * en septiembre.
 */
export interface OtQueryDateFilter {
  campo: OtQueryDateFieldId;
  preset: OtQueryRangePreset;
  from?: string | null;
  to?: string | null;
}

export interface OtQueryDefinition {
  fechas: OtQueryDateFilter;
  condiciones: OtQueryCondition[];
  columnas: string[];
  sortBy?: string | null;
  descending?: boolean;
}

export const DATE_FIELD_LABEL: Record<OtQueryDateFieldId, string> = {
  radicacion: "Fecha de radicación",
  decision: "Fecha de decisión",
  actualizacion: "Última actualización",
};

export const RANGE_PRESETS: { value: OtQueryRangePreset; label: string }[] = [
  { value: "hoy", label: "Hoy" },
  { value: "ultimos_7", label: "Últimos 7 días" },
  { value: "ultimos_30", label: "Últimos 30 días" },
  { value: "ultimos_90", label: "Últimos 90 días" },
  { value: "mes_actual", label: "Mes actual" },
  { value: "mes_anterior", label: "Mes anterior" },
  { value: "anio_actual", label: "Año actual" },
  { value: "personalizado", label: "Rango propio" },
];

// ── Resultado ───────────────────────────────────────────────────────────────────

export type OtQueryCoverageResult = "encontrado" | "excluido" | "no_existe";

/**
 * Qué pasó con cada valor que el usuario pidió por nombre. Sin esto, un resultado con menos filas
 * de las esperadas se lee como «se perdió un dato».
 */
export interface OtQueryCoverageItem {
  campo: string;
  valor: string;
  resultado: OtQueryCoverageResult;
  motivoCampo: string | null;
  motivo: string | null;
}

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
  actualizadoEn: string | null;
  decididoPor: string | null;
  horasHastaDecision: number | null;
  diasEnOrganismo: number | null;
  devoluciones: number;
  causalesUltimoRechazo: string[];
}

export interface OtQueryResult {
  total: number;
  page: number;
  pageSize: number;
  desde: string;
  hasta: string;
  totalPeriodoAnterior: number;
  filas: OtQueryRow[];
  cobertura: OtQueryCoverageItem[];
}

export interface OtSavedQuery {
  id: string;
  nombre: string;
  descripcion: string | null;
  /** Las de fábrica no se editan ni se borran: guardarlas las duplica. */
  deFabrica: boolean;
  definition: OtQueryDefinition;
  createdAt: string;
  updatedAt: string | null;
}

export const OT_QUERY_PAGE_SIZE = 50;
export const OT_QUERY_MAX_PAGE_SIZE = 200;

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
