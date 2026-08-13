// Cliente tipado de las consultas de la empresa gestora: el usuario arma su propia búsqueda sobre
// los trámites que su empresa gestiona, la guarda y la exporta.
//
// El modelo de la consulta es el MISMO que el del organismo (`./queries`). Lo propio de aquí es la
// fila —una gestora mira su ciclo, no la bandeja del organismo—, sus fechas y la ruta.
//
// El catálogo de campos NO está escrito aquí. Lo sirve el backend y esta capa solo lo transporta:
// es lo que hace que agregar un campo consultable sea tocar un archivo del servidor y verlo
// aparecer en el constructor sin desplegar frontend.
import { apiFetch } from "./client";
import type {
  QueryDefinition,
  QueryField,
  QueryResult,
  SaveQueryInput,
  SavedQuery,
} from "./queries";
import { QUERY_PAGE_SIZE } from "./queries";

const base = "/api/v1/analytics/queries";

/**
 * Sobre qué fecha del trámite se aplica el rango.
 *
 * Son otras que las del organismo y por una razón concreta: la gestora vive el trámite desde antes
 * —lo arma en borrador— y el organismo solo lo conoce desde que se lo radican. «Los traspasos de
 * julio» es una pregunta distinta según se cuente por cuándo se empezó a preparar o por cuándo se
 * entregó.
 */
export const COMPANY_DATE_FIELDS: { value: string; label: string }[] = [
  { value: "creacion", label: "Fecha de creación" },
  { value: "envio", label: "Fecha de envío al organismo" },
  { value: "cierre", label: "Fecha de cierre" },
  { value: "actualizacion", label: "Última actualización" },
];

export interface CompanyQueryRow {
  procedureInstanceId: string;
  referenceNumber: string;
  placa: string | null;
  vin: string | null;
  transitOfficeId: string | null;
  transitOfficeName: string | null;
  /**
   * La compañía dueña del trámite. Se llena siempre, pero solo importa cuando la consulta corre
   * sobre varias compañías a la vez — ver `lib/api/superadmin-queries.ts`.
   */
  companiaId: string;
  companiaNombre: string;
  procedureTypeId: string;
  procedureTypeName: string;
  modalidad: string;
  status: string;
  prioritario: boolean;
  subsanacionActiva: boolean;
  subsanacionCount: number;
  comprador: string | null;
  vendedor: string | null;
  tienePrenda: boolean;
  acreedorPrenda: string | null;
  tieneLicenciaTransito: boolean;
  transformaciones: string[];
  esLeasing: boolean;
  metodoPago: string | null;
  tipoTraspaso: string;
  radicadoPor: string;
  creadoEn: string;
  enviadoEn: string | null;
  cerradoEn: string | null;
  actualizadoEn: string | null;
  diasHastaEnvio: number | null;
  diasEnOrganismo: number | null;
  devoluciones: number;
}

export type CompanyQueryResult = QueryResult<CompanyQueryRow>;

/**
 * SuperAdmin no tiene empresa propia y el backend le exige decir cuál mira; el resto de usuarios va
 * con la suya y no manda nada.
 */
function tenantQuery(tenantId?: string) {
  return tenantId ? { tenantId } : undefined;
}

export function fetchCompanyQueryFields(
  tenantId?: string,
  signal?: AbortSignal,
): Promise<QueryField[]> {
  return apiFetch<QueryField[]>(`${base}/fields`, { query: tenantQuery(tenantId), signal });
}

/**
 * La ejecución va por POST aunque sea una lectura: la definición lleva listas de placas pegadas
 * desde Excel, y meterlas en la barra de direcciones las expondría en los registros de los proxies
 * y chocaría con el límite de longitud de URL a las pocas decenas.
 */
export function runCompanyQuery(
  definition: QueryDefinition,
  options: { page?: number; pageSize?: number; tenantId?: string; signal?: AbortSignal } = {},
): Promise<CompanyQueryResult> {
  return apiFetch<CompanyQueryResult>(`${base}/run`, {
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

export function fetchCompanySavedQueries(
  tenantId?: string,
  signal?: AbortSignal,
): Promise<SavedQuery[]> {
  return apiFetch<SavedQuery[]>(`${base}/saved`, { query: tenantQuery(tenantId), signal });
}

export function saveCompanyQuery(
  input: SaveQueryInput,
  tenantId?: string,
): Promise<SavedQuery> {
  return apiFetch<SavedQuery>(`${base}/saved`, {
    method: "POST",
    body: input,
    query: tenantQuery(tenantId),
  });
}

export function deleteCompanySavedQuery(id: string, tenantId?: string): Promise<void> {
  return apiFetch<void>(`${base}/saved/${id}`, {
    method: "DELETE",
    query: tenantQuery(tenantId),
  });
}
