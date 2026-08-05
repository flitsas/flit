// Cliente tipado de los reportes del organismo de tránsito y del catálogo de causales de rechazo.
//
// El eje de estos reportes está INVERTIDO respecto a los de empresa: allí un tenant mira hacia
// varios organismos; aquí un organismo mira hacia las empresas que le radican.
import { apiFetch } from "./client";
import type { RejectionReason, SaveRejectionReasonRequest } from "./types-ot";

const base = "/api/v1/admin/ot/metrics";
const reasonsBase = "/api/v1/admin/rejection-reasons";

export interface OtMetricsParams {
  from: string;
  to: string;
  modalidad?: string;
  clientTenantId?: string;
  /** Override de SuperAdmin para ver el reporte de un organismo concreto. */
  transitOfficeId?: string;
}

// ── A.1 Panel operativo ─────────────────────────────────────────────────────────

export interface OtDayMovement {
  entregadosHoy: number;
  decididosHoy: number;
  pendientesTotal: number;
  tiempoMedianoDecisionHoras: number | null;
}

export interface OtQueueBreakdown {
  porRevisar: number;
  esperandoAsignarPlaca: number;
  /**
   * Pendientes que esperan algo del cliente (SOAT, o pausados). No se desglosa: se devuelve como
   * un solo número para poder explicar por qué el desglose no suma el total, y para poder decidir
   * más adelante si se excluyen del conteo.
   */
  enEsperaDelCliente: number;
}

export interface OtAging {
  hasta1Dia: number;
  entre2y3Dias: number;
  entre4y7Dias: number;
  masDe7Dias: number;
  prioritariosEstancados: number;
}

export interface OtOperationalPanel {
  movimiento: OtDayMovement;
  cola: OtQueueBreakdown;
  antiguedad: OtAging;
}

// ── A.2 Desempeño ───────────────────────────────────────────────────────────────

export interface OtReviewer {
  userId: string;
  displayName: string;
  decididos: number;
  aprobados: number;
  aprobacionPct: number;
  rechazados: number;
  rechazoPct: number;
  tiempoMedianoHoras: number | null;
  vuelvenARechazarsePct: number;
}

export interface OtClientCompanyQuality {
  tenantId: string;
  name: string;
  entregados: number;
  /**
   * Base de `pasanPrimeraPct`. Con cero aprobados no hay nada que medir: la consola muestra «—»
   * en vez de un «100 %» sin base, que se leería como una empresa impecable.
   */
  aprobados: number;
  pasanPrimeraPct: number;
  devolucionesPromedio: number;
}

export interface OtPerformance {
  revisores: OtReviewer[];
  empresas: OtClientCompanyQuality[];
}

// ── A.3 Motivos de rechazo ──────────────────────────────────────────────────────

export interface OtRejectionReasonStat {
  reasonId: string;
  code: string;
  description: string;
  rechazos: number;
  /** % de RECHAZOS que incluyen esta causal. Puede sumar más de 100 % entre todas. */
  pct: number;
}

export interface OtRejectionReasons {
  causales: OtRejectionReasonStat[];
  totalRechazos: number;
  /** Rechazos sin ninguna causal marcada (incluye los anteriores al catálogo). */
  rechazosSinCausal: number;
  /**
   * Indicador de salud del dato: si se acerca al tamaño del catálogo, alguien está marcando todo
   * y la distribución deja de discriminar.
   */
  promedioCausalesPorRechazo: number;
}

// ── Drill-down: de qué está hecho cada número ────────────────────────────────────

/**
 * Bloques del panel que se pueden abrir. Mismos ids que `OtDrilldownBuckets` en el backend: un
 * bucket desconocido devolvería 400, así que ambos lados deben coincidir.
 */
export const OT_DRILLDOWN_BUCKETS = {
  entregadosHoy: "entregados_hoy",
  decididosHoy: "decididos_hoy",
  pendientes: "pendientes",
  porRevisar: "por_revisar",
  esperandoPlaca: "esperando_placa",
  enEsperaDelCliente: "espera_cliente",
  hasta1Dia: "antiguedad_0_1",
  entre2y3Dias: "antiguedad_2_3",
  entre4y7Dias: "antiguedad_4_7",
  masDe7Dias: "antiguedad_mas_7",
  prioritariosEstancados: "prioritarios_estancados",
} as const;

export type OtDrilldownBucket = (typeof OT_DRILLDOWN_BUCKETS)[keyof typeof OT_DRILLDOWN_BUCKETS];

/** Un trámite dentro de un bloque, con lo justo para reconocerlo y poder ir a decidirlo. */
export interface OtDrilldownItem {
  procedureInstanceId: string;
  referenceNumber: string;
  placa: string | null;
  vin: string | null;
  clientTenantId: string;
  clientTenantName: string;
  status: string;
  modalidadEntrada: string | null;
  prioritario: boolean;
  diasEsperando: number | null;
}

export interface OtDrilldown {
  bucket: string;
  total: number;
  omitidos: number;
  items: OtDrilldownItem[];
}

/** Empresa cliente del organismo, para el filtro del reporte. */
export interface OtClientCompanyOption {
  tenantId: string;
  name: string;
}

// ── B. Informe del periodo ──────────────────────────────────────────────────────

/**
 * Estados del informe. NO son los estados crudos del trámite: son su lectura DESDE el organismo.
 * Borrador y preparado no existen aquí porque el organismo nunca los vio, y `rechazado` se parte
 * según si el gestor ya abrió la subsanación — para el organismo, uno vuelve y el otro no.
 *
 * Los buckets son excluyentes y exhaustivos: su suma es siempre el total del informe.
 */
export const OT_REPORT_ESTADOS = {
  enRevision: "en_revision",
  esperandoPlaca: "esperando_placa",
  esperandoCliente: "esperando_cliente",
  aprobado: "aprobado",
  enSubsanacion: "en_subsanacion",
  rechazado: "rechazado",
  anulado: "anulado",
  otro: "otro",
} as const;

export type OtReportEstado = (typeof OT_REPORT_ESTADOS)[keyof typeof OT_REPORT_ESTADOS];

export interface OtReportTimeBucket {
  key: string;
  label: string;
  tramites: number;
}

export interface OtReportSeriesPoint {
  bucket: string;
  label: string;
  /** Primer y último día del periodo, ya recortados contra el rango pedido (`yyyy-MM-dd`). */
  desde: string;
  hasta: string;
  radicados: number;
  aprobados: number;
  rechazados: number;
}

export interface OtReportSummary {
  total: number;
  enRevision: number;
  esperandoPlaca: number;
  esperandoCliente: number;
  aprobados: number;
  enSubsanacion: number;
  rechazados: number;
  anulados: number;
  otros: number;
  decididos: number;
  devoluciones: number;
  devolucionesPromedio: number;
  /** Mediana del turno del organismo (última radicación → decisión), en horas. */
  tiempoMedianoHoras: number | null;
  tiempoPromedioHoras: number | null;
  /** p90: el compromiso que el organismo puede sostener, no el caso típico. */
  tiempoP90Horas: number | null;
  tiempoMedianoAprobacionHoras: number | null;
  distribucionTiempos: OtReportTimeBucket[];
  /** "dia" | "semana" | "mes". La elige el servidor según el ancho del rango. */
  granularidad: string;
  serie: OtReportSeriesPoint[];
}

export interface OtReportRow {
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
  radicadoEn: string;
  ultimaRadicacionEn: string | null;
  decididoEn: string | null;
  decididoPor: string | null;
  horasHastaDecision: number | null;
  diasEnOrganismo: number | null;
  devoluciones: number;
  causalesUltimoRechazo: string[];
}

export interface OtReport {
  resumen: OtReportSummary;
  /** Universo completo, no las filas devueltas: el resumen nunca describe solo la página. */
  total: number;
  page: number;
  pageSize: number;
  filas: OtReportRow[];
}

/** Campos por los que el backend sabe ordenar. Uno desconocido cae al orden por defecto, no falla. */
export const OT_REPORT_SORT = {
  radicado: "radicado",
  decidido: "decidido",
  dias: "dias",
  empresa: "empresa",
  referencia: "referencia",
  devoluciones: "devoluciones",
  estado: "estado",
} as const;

export type OtReportSort = (typeof OT_REPORT_SORT)[keyof typeof OT_REPORT_SORT];

export interface OtReportParams extends OtMetricsParams {
  page?: number;
  pageSize?: number;
  sortBy?: OtReportSort;
  desc?: boolean;
}

// ── Llamadas ────────────────────────────────────────────────────────────────────

function toQuery(params: OtMetricsParams): Record<string, string> {
  const query: Record<string, string> = { from: params.from, to: params.to };
  if (params.modalidad) query.modalidad = params.modalidad;
  if (params.clientTenantId) query.clientTenantId = params.clientTenantId;
  if (params.transitOfficeId) query.transitOfficeId = params.transitOfficeId;
  return query;
}

export function fetchOtOperationalPanel(params: OtMetricsParams): Promise<OtOperationalPanel> {
  return apiFetch<OtOperationalPanel>(`${base}/operational`, { query: toQuery(params) });
}

export function fetchOtPerformance(params: OtMetricsParams): Promise<OtPerformance> {
  return apiFetch<OtPerformance>(`${base}/performance`, { query: toQuery(params) });
}

export function fetchOtRejectionReasons(params: OtMetricsParams): Promise<OtRejectionReasons> {
  return apiFetch<OtRejectionReasons>(`${base}/rejection-reasons`, { query: toQuery(params) });
}

/**
 * Abre un número del panel: los trámites que lo componen, con los mismos predicados con que se
 * contó la tarjeta. `omitidos` dice cuántos quedaron fuera del tope de filas del backend.
 */
export function fetchOtDrilldown(
  params: OtMetricsParams,
  bucket: OtDrilldownBucket,
): Promise<OtDrilldown> {
  return apiFetch<OtDrilldown>(`${base}/drilldown`, { query: { ...toQuery(params), bucket } });
}

/**
 * Informe del periodo. El universo son los trámites RECIBIDOS en el rango, no los decididos: es
 * lo que permite que el desglose por estado cierre contra el total.
 */
export function fetchOtReport(params: OtReportParams, signal?: AbortSignal): Promise<OtReport> {
  const query: Record<string, string> = toQuery(params);
  if (params.page) query.page = String(params.page);
  if (params.pageSize) query.pageSize = String(params.pageSize);
  if (params.sortBy) query.sortBy = params.sortBy;
  if (params.desc !== undefined) query.desc = String(params.desc);
  return apiFetch<OtReport>(`${base}/report`, { query, signal });
}

/** Empresas cliente del organismo, para poblar el filtro del reporte. */
export function fetchOtClientCompanies(
  transitOfficeId?: string,
): Promise<OtClientCompanyOption[]> {
  const query: Record<string, string> = {};
  if (transitOfficeId) query.transitOfficeId = transitOfficeId;
  return apiFetch<OtClientCompanyOption[]>(`${base}/client-companies`, { query });
}

// ── Catálogo de causales ────────────────────────────────────────────────────────

export function fetchRejectionReasons(options?: {
  modalidad?: string;
  includeInactive?: boolean;
}): Promise<RejectionReason[]> {
  const query: Record<string, string> = {};
  if (options?.modalidad) query.modalidad = options.modalidad;
  if (options?.includeInactive) query.includeInactive = "true";
  return apiFetch<RejectionReason[]>(reasonsBase, { query });
}

export function createRejectionReason(
  body: SaveRejectionReasonRequest,
): Promise<RejectionReason> {
  return apiFetch<RejectionReason>(reasonsBase, { method: "POST", body });
}

export function updateRejectionReason(
  id: string,
  body: SaveRejectionReasonRequest,
): Promise<RejectionReason> {
  return apiFetch<RejectionReason>(`${reasonsBase}/${id}`, { method: "PUT", body });
}

/** No hay borrado: una causal retirada sigue resolviendo el nombre de los rechazos históricos. */
export function setRejectionReasonActive(
  id: string,
  isActive: boolean,
): Promise<RejectionReason> {
  return apiFetch<RejectionReason>(`${reasonsBase}/${id}/active`, {
    method: "PATCH",
    body: { isActive },
  });
}
