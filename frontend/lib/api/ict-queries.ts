// Cliente tipado de las consultas de la empresa sobre sus pre-trámites de Integración con
// Terceros (ICT): el usuario arma su propia búsqueda sobre el pipeline de validación, la guarda y
// la exporta. El gemelo, un paso antes del trámite, de `./company-queries`.
//
// El modelo de la consulta es COMPARTIDO con las otras dos consolas (`./queries`). Aquí solo va lo
// propio de ICT: cómo es su fila, sobre qué fechas filtra y a qué endpoint llama.
//
// El catálogo de campos NO está escrito aquí. Lo sirve el backend (`IctQueriesEndpoints`,
// `/api/v1/analytics/ict-queries`) y esta capa solo lo transporta: agregar un campo consultable es
// tocar un archivo del servidor, no desplegar frontend.
import { apiFetch } from "./client";
import type {
  QueryDefinition,
  QueryField,
  QueryResult,
  SaveQueryInput,
  SavedQuery,
} from "./queries";
import { QUERY_PAGE_SIZE } from "./queries";

const base = "/api/v1/analytics/ict-queries";

/**
 * Sobre qué fecha del pre-trámite se aplica el rango.
 *
 * Son otras que las del trámite (organismo o empresa) porque ICT vive un momento anterior: lo que
 * interesa aquí es cuándo se registró y cuándo pasó cada validación, no cuándo se entregó o se
 * cerró — eso todavía no existe mientras el pre-trámite sigue en este pipeline.
 */
export const ICT_DATE_FIELDS: { value: string; label: string }[] = [
  { value: "registro", label: "Fecha de registro" },
  { value: "validacion_negocio", label: "Fecha de validación de negocio" },
  { value: "validacion_externa", label: "Fecha de validación externa" },
];

/** Una fila del resultado: un pre-trámite de ICT. */
export interface IctQueryRow {
  id: string;
  transactionNumber: number;
  radicado: string | null;
  placa: string | null;
  vin: string | null;
  tenantId: string;
  tenantNombre: string;
  tipoTramite: string | null;
  estado: string;
  tieneNovedades: boolean;
  tieneBorrador: boolean;
  prioritario: boolean;
  secretaria: string | null;
  clienteIntegracion: string | null;
  comentarios: string | null;
  procedureInstanceId: string | null;
  registradoEn: string;
  validacionNegocioEn: string | null;
  validacionExternaEn: string | null;
}

export type IctQueryResult = QueryResult<IctQueryRow>;

/**
 * SuperAdmin no tiene compañía propia y el backend le exige decir cuál mira; el resto de usuarios
 * va con la suya y no manda nada.
 */
function tenantQuery(tenantId?: string) {
  return tenantId ? { tenantId } : undefined;
}

export function fetchIctQueryFields(
  tenantId?: string,
  signal?: AbortSignal,
): Promise<QueryField[]> {
  return apiFetch<QueryField[]>(`${base}/fields`, { query: tenantQuery(tenantId), signal });
}

/**
 * La ejecución va por POST aunque sea una lectura: la definición lleva listas de placas, VIN o
 * radicados pegadas desde Excel, y meterlas en la barra de direcciones las expondría en los
 * registros de los proxies y chocaría con el límite de longitud de URL a las pocas decenas.
 */
export function runIctQuery(
  definition: QueryDefinition,
  options: { page?: number; pageSize?: number; tenantId?: string; signal?: AbortSignal } = {},
): Promise<IctQueryResult> {
  return apiFetch<IctQueryResult>(`${base}/run`, {
    method: "POST",
    body: {
      definition,
      page: options.page ?? 1,
      pageSize: options.pageSize ?? QUERY_PAGE_SIZE,
    },
    query: tenantQuery(options.tenantId),
    signal: options.signal,
  });
}

export function fetchIctSavedQueries(
  tenantId?: string,
  signal?: AbortSignal,
): Promise<SavedQuery[]> {
  return apiFetch<SavedQuery[]>(`${base}/saved`, { query: tenantQuery(tenantId), signal });
}

export function saveIctQuery(input: SaveQueryInput, tenantId?: string): Promise<SavedQuery> {
  return apiFetch<SavedQuery>(`${base}/saved`, {
    method: "POST",
    body: input,
    query: tenantQuery(tenantId),
  });
}

export function deleteIctSavedQuery(id: string, tenantId?: string): Promise<void> {
  return apiFetch<void>(`${base}/saved/${id}`, {
    method: "DELETE",
    query: tenantQuery(tenantId),
  });
}
