// Cliente de los reportes de ICT en vivo (HU #11619) — misma agregación que ya arma el Excel
// del informe programado (HU #11617), expuesta en JSON para verla en pantalla sin programar nada.
import { apiFetch } from "./client";

const base = "/api/v1/analytics/ict-reports";

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

export interface IctNovedadesReport {
  resumenPorCausa: IctCausaResumen[];
  detalle: IctNovedadDetalle[];
  total: number;
  truncated: boolean;
}

export interface IctAtascado {
  placa: string | null;
  vin: string | null;
  radicado: string | null;
  esperando: string;
  diasTranscurridos: number;
}

export interface IctAtascadosReport {
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

export interface IctJobsReport {
  resumenPorJob: IctJobResumen[];
  corridasFueraDeSla: IctJobIncumplido[];
  total: number;
  truncated: boolean;
}

export interface IctWebhook {
  radicado: string;
  estado: string;
  intentos: number;
  urlDestino: string | null;
  registradoEn: string;
}

export interface IctWebhooksReport {
  detalle: IctWebhook[];
  total: number;
  truncated: boolean;
}

export interface IctReportDateRange {
  from: string;
  to: string;
}

export function fetchIctNovedadesReport(
  range: IctReportDateRange,
  tenantId?: string,
  signal?: AbortSignal,
): Promise<IctNovedadesReport> {
  return apiFetch<IctNovedadesReport>(`${base}/novedades`, {
    query: { from: range.from, to: range.to, tenantId },
    signal,
  });
}

export function fetchIctAtascadosReport(
  tenantId?: string,
  signal?: AbortSignal,
): Promise<IctAtascadosReport> {
  return apiFetch<IctAtascadosReport>(`${base}/atascados`, {
    query: { tenantId },
    signal,
  });
}

/** Solo SuperAdmin: `ict.job_runs` es una tabla de plataforma, sin `tenant_id`. */
export function fetchIctJobsReport(
  range: IctReportDateRange,
  signal?: AbortSignal,
): Promise<IctJobsReport> {
  return apiFetch<IctJobsReport>(`${base}/jobs`, {
    query: { from: range.from, to: range.to },
    signal,
  });
}

export function fetchIctWebhooksReport(
  range: IctReportDateRange,
  tenantId?: string,
  signal?: AbortSignal,
): Promise<IctWebhooksReport> {
  return apiFetch<IctWebhooksReport>(`${base}/webhooks`, {
    query: { from: range.from, to: range.to, tenantId },
    signal,
  });
}
