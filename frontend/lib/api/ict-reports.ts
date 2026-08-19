// Cliente de los reportes de ICT en vivo (HU #11619) — misma agregación que ya arma el Excel
// del informe programado (HU #11617), expuesta en JSON para verla en pantalla sin programar nada.
import { apiFetch } from "./client";
import { downloadFile } from "./download";

const base = "/api/v1/analytics/ict-reports";

/**
 * Filas por página del detalle en vivo. Es el `pageSize` por defecto del backend
 * (`QueryLimits.DefaultPageSize`); se manda explícito para que la UI no dependa de él.
 */
export const ICT_REPORT_PAGE_SIZE = 50;

/**
 * Tope de filas del Excel (`IctOwnReportDocumentBuilder.MaxRows`). Es lo único que sigue
 * cortándose: el detalle en pantalla ya no se topa, se pagina.
 */
export const ICT_EXCEL_MAX_ROWS = 2_000;

/** Página del detalle (1-based), con la misma convención que el motor de consultas. */
export interface IctReportPaging {
  page?: number;
  pageSize?: number;
}

/** Campos de paginación que devuelven los 4 reportes: la página EFECTIVA ya normalizada. */
interface IctPagedResult {
  page: number;
  pageSize: number;
}

export interface IctCausaResumen {
  causa: string;
  cantidad: number;
  porcentajeTexto: string;
}

export interface IctNovedadDetalle {
  placa: string | null;
  vin: string | null;
  radicado: string | null;
  comentarios: string | null;
  registradoEn: string;
}

export interface IctNovedadesReport extends IctPagedResult {
  /** Resumen del periodo COMPLETO: no cambia al pasar de página. */
  resumenPorCausa: IctCausaResumen[];
  /** Solo la página pedida (`detalle.length <= pageSize`). */
  detalle: IctNovedadDetalle[];
  /** Universo del periodo (conteo real), no el largo de `detalle`. */
  total: number;
  /** `true` si el Excel de este informe se corta en {@link ICT_EXCEL_MAX_ROWS} filas. */
  truncated: boolean;
  /** Total del periodo inmediatamente anterior, de la misma longitud, para la variación. */
  totalPeriodoAnterior: number;
}

export interface IctAtascado {
  placa: string | null;
  vin: string | null;
  radicado: string | null;
  esperando: string;
  diasTranscurridos: number;
}

export interface IctAtascadosReport extends IctPagedResult {
  detalle: IctAtascado[];
  total: number;
  truncated: boolean;
}

export interface IctJobResumen {
  job: string;
  corridas: number;
  duracionPromedioSeg: number;
  duracionMaximaSeg: number;
  porcentajeFueraDeSlaTexto: string;
}

export interface IctJobIncumplido {
  job: string;
  resultado: string;
  duracionSeg: number;
  inicio: string;
}

export interface IctJobsReport extends IctPagedResult {
  /** Resumen del periodo COMPLETO: no cambia al pasar de página. */
  resumenPorJob: IctJobResumen[];
  /** Solo la página pedida; su universo es {@link IctJobsReport.totalFueraDeSla}. */
  corridasFueraDeSla: IctJobIncumplido[];
  /** Corridas del periodo (universo del KPI y de la variación). */
  total: number;
  /**
   * `true` si el Excel se corta en {@link ICT_EXCEL_MAX_ROWS} filas. Se calcula sobre
   * `totalFueraDeSla`, que es el universo de la única hoja que puede cortarse — el resumen por job
   * trae una fila por job. Mismo significado que en los otros tres reportes.
   */
  truncated: boolean;
  totalPeriodoAnterior: number;
  /** Universo de `corridasFueraDeSla` en el periodo. */
  totalFueraDeSla: number;
}

export interface IctWebhook {
  radicado: string;
  estado: string;
  intentos: number;
  urlDestino: string | null;
  registradoEn: string;
}

export interface IctWebhooksReport extends IctPagedResult {
  /** Solo la página pedida. */
  detalle: IctWebhook[];
  /** Universo del periodo (conteo real), no el largo de `detalle`. */
  total: number;
  /** `true` si el Excel de este informe se corta en {@link ICT_EXCEL_MAX_ROWS} filas. */
  truncated: boolean;
  totalPeriodoAnterior: number;
  // Reparto por estado del PERIODO COMPLETO (no de la página). Los tres suman `total` siempre:
  // `is_notified` y `response_ok` son NOT NULL con default `false` en el esquema, así que no hay
  // una cuarta categoría posible y los porcentajes se pueden sacar sin defensa extra.
  /** Entregados en el periodo (`is_notified AND response_ok`). */
  totalEntregados: number;
  /** Fallidos en el periodo (`is_notified AND NOT response_ok`). */
  totalFallidos: number;
  /** Pendientes en el periodo (aún sin notificar). */
  totalPendientes: number;
}

export interface IctReportDateRange {
  from: string;
  to: string;
}

export function fetchIctNovedadesReport(
  range: IctReportDateRange,
  tenantId?: string,
  paging?: IctReportPaging,
  signal?: AbortSignal,
): Promise<IctNovedadesReport> {
  return apiFetch<IctNovedadesReport>(`${base}/novedades`, {
    query: { from: range.from, to: range.to, tenantId, ...pagingQuery(paging) },
    signal,
  });
}

export function fetchIctAtascadosReport(
  tenantId?: string,
  paging?: IctReportPaging,
  signal?: AbortSignal,
): Promise<IctAtascadosReport> {
  return apiFetch<IctAtascadosReport>(`${base}/atascados`, {
    query: { tenantId, ...pagingQuery(paging) },
    signal,
  });
}

/** Solo SuperAdmin: `ict.job_runs` es una tabla de plataforma, sin `tenant_id`. */
export function fetchIctJobsReport(
  range: IctReportDateRange,
  paging?: IctReportPaging,
  signal?: AbortSignal,
): Promise<IctJobsReport> {
  return apiFetch<IctJobsReport>(`${base}/jobs`, {
    query: { from: range.from, to: range.to, ...pagingQuery(paging) },
    signal,
  });
}

export function fetchIctWebhooksReport(
  range: IctReportDateRange,
  tenantId?: string,
  paging?: IctReportPaging,
  signal?: AbortSignal,
): Promise<IctWebhooksReport> {
  return apiFetch<IctWebhooksReport>(`${base}/webhooks`, {
    query: { from: range.from, to: range.to, tenantId, ...pagingQuery(paging) },
    signal,
  });
}

/** `page`/`pageSize` para la query; se omiten si no se pide paginación (el backend pone su default). */
function pagingQuery(paging?: IctReportPaging): { page?: number; pageSize?: number } {
  return { page: paging?.page, pageSize: paging?.pageSize };
}

// ── Exportación a Excel ──────────────────────────────────────────────────────────────────────
//
// Descarga EL MISMO archivo que llega adjunto al informe programado, pero bajo demanda: hasta
// ahora, para tener el Excel había que programar un correo y esperar a que corriera el envío.

export function exportIctNovedadesReport(
  range: IctReportDateRange,
  tenantId?: string,
  signal?: AbortSignal,
): Promise<void> {
  return downloadFile(`${base}/novedades/export`, {
    query: { from: range.from, to: range.to, tenantId },
    fallbackFilename: `ict_novedades_${range.from}_${range.to}.xlsx`,
    signal,
  });
}

export function exportIctAtascadosReport(tenantId?: string, signal?: AbortSignal): Promise<void> {
  return downloadFile(`${base}/atascados/export`, {
    query: { tenantId },
    fallbackFilename: "ict_atascados.xlsx",
    signal,
  });
}

/** Solo SuperAdmin, igual que el reporte en vivo. */
export function exportIctJobsReport(range: IctReportDateRange, signal?: AbortSignal): Promise<void> {
  return downloadFile(`${base}/jobs/export`, {
    query: { from: range.from, to: range.to },
    fallbackFilename: `ict_jobs_${range.from}_${range.to}.xlsx`,
    signal,
  });
}

export function exportIctWebhooksReport(
  range: IctReportDateRange,
  tenantId?: string,
  signal?: AbortSignal,
): Promise<void> {
  return downloadFile(`${base}/webhooks/export`, {
    query: { from: range.from, to: range.to, tenantId },
    fallbackFilename: `ict_webhooks_${range.from}_${range.to}.xlsx`,
    signal,
  });
}
