// Cliente del Inicio del SuperAdmin (rediseño de los 3 dashboards, 2026-11-17): lecturas ENTRE compañías que solo el
// SuperAdmin puede pedir (`SuperAdminDashboardEndpoints.cs`; cualquier otro rol recibe 403).
import { apiFetch } from "./client";

const base = "/api/v1/analytics/superadmin";

/** Rango opcional: un extremo vacío NO viaja (BUG #12588, sin rango = universo completo). */
export interface RangoSuperAdmin {
  from?: string;
  to?: string;
}

/**
 * Una fila de «Resumen de compañías activas». Vocabulario de estados (igual en todo el Inicio del SuperAdmin):
 * activos = en proceso (ni aprobado, ni rechazado, ni anulado, ni revocado); la tasa es
 * aprobados / (aprobados + rechazados) y viene `null` sin decididos; represados = en proceso sin moverse en más de
 * `stuckDays` días.
 */
export interface SuperAdminCompania {
  tenantId: string;
  nombre: string;
  total: number;
  activos: number;
  aprobados: number;
  rechazados: number;
  tasaAprobacionPct: number | null;
  represados: number;
}

export interface SuperAdminCompaniasResponse {
  from: string | null;
  to: string | null;
  stuckDays: number;
  items: SuperAdminCompania[];
}

export type CategoriaOperacion = "matriculas" | "traspasos" | "otros";

/** Completados (aprobado) / En proceso / Rechazados de una familia. */
export interface DistribucionFamilia {
  categoria: CategoriaOperacion | string;
  completados: number;
  enProceso: number;
  rechazados: number;
}

/** Una tarjeta de «Rendimiento por Organismo de Tránsito». El tiempo va en días, de la radicación a la decisión. */
export interface SuperAdminOrganismo {
  transitOfficeId: string;
  nombre: string;
  volumen: number;
  decididos: number;
  tiempoPromedioDias: number | null;
  distribucion: DistribucionFamilia[];
}

/**
 * `tiempoPromedioDias` es el «Tiempo de respuesta del organismo» (radicación → aprobación o rechazo): el promedio de TODAS las
 * decisiones (aprobado/rechazado), cada una medida desde la última radicación ('entregado') anterior. Equivale al
 * promedio de las tarjetas ponderado por sus decididos.
 */
export interface SuperAdminOrganismosResponse {
  tenantId: string | null;
  from: string | null;
  to: string | null;
  decididos: number;
  tiempoPromedioDias: number | null;
  items: SuperAdminOrganismo[];
}

/** GET /companias-resumen — una fila por compañía activa (gestoras; los organismos de tránsito no salen). */
export function fetchSuperAdminCompanias(
  rango: RangoSuperAdmin & { stuckDays: number },
  signal?: AbortSignal,
): Promise<SuperAdminCompaniasResponse> {
  return apiFetch<SuperAdminCompaniasResponse>(`${base}/companias-resumen`, {
    // `stuckDays` viaja SIEMPRE: el texto «más de N días» del Inicio no depende del valor por defecto del servidor.
    query: { from: rango.from || undefined, to: rango.to || undefined, stuckDays: rango.stuckDays },
    signal,
  });
}

/** GET /organismos — sin `tenantId` cuenta todas las compañías. */
export function fetchSuperAdminOrganismos(
  params: RangoSuperAdmin & { tenantId?: string },
  signal?: AbortSignal,
): Promise<SuperAdminOrganismosResponse> {
  return apiFetch<SuperAdminOrganismosResponse>(`${base}/organismos`, {
    query: { from: params.from || undefined, to: params.to || undefined, tenantId: params.tenantId || undefined },
    signal,
  });
}
