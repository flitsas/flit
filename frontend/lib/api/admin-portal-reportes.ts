// HU #14399 (Feature #14396, Épica #14297) — cliente del REPORTE de trámites del portal Persona Natural. Contrato:
// `docs/design/EPICA-14297-contrato-resto.md` (sección Reportes).
//   OT:          GET /api/v1/admin/ot/portal-reportes                    y  …/export
//   Plataforma:  GET /api/v1/admin/plataforma/persona-natural/reportes   y  …/export
// El `ot_admin` no envía el organismo (sale de su token); el SuperAdmin del hub lo envía siempre y el de Plataforma
// puede filtrarlo con `transitOfficeId`.
import { apiFetch } from "./client";
import { downloadFile } from "./download";

export const OT_PORTAL_REPORTES_BASE = "/api/v1/admin/ot/portal-reportes";
export const PLATAFORMA_PORTAL_REPORTES_BASE = "/api/v1/admin/plataforma/persona-natural/reportes";

export interface FilaReportePortal {
  canal: string;
  organismo: string | null;
  tipo: string;
  radicado: string | null;
  estado: string;
  fechaCaida: string;
  modalidad: string | null;
  valorTramite: number | null;
  valorGestion: number | null;
}

export interface PaginaReportePortal {
  items: FilaReportePortal[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface FiltroReportePortal {
  /** Código de canal (`pn_web`, `pn_punto`, `aplicacion`); si se eligen varios, separados por coma. */
  canal?: string;
  tipoTramiteId?: string;
  /** Fecha de calendario `AAAA-MM-DD`. */
  desde?: string;
  hasta?: string;
  /** Solo lo atiende el servidor para el SuperAdmin. */
  transitOfficeId?: string;
  page?: number;
  pageSize?: number;
}

export interface OpcionReporte {
  id: string;
  nombre: string;
}

/** Cliente inyectable del componente del reporte: el mismo para el hub del OT y para Plataforma. */
export interface ReportePortalApi {
  listar: (filtro: FiltroReportePortal, signal?: AbortSignal) => Promise<PaginaReportePortal>;
  /** Excel con los mismos filtros (sin paginación). */
  exportar: (filtro: FiltroReportePortal) => Promise<void>;
  tipos: (signal?: AbortSignal) => Promise<OpcionReporte[]>;
  /** Solo Plataforma: organismos para el filtro «Organismo». */
  organismos?: (signal?: AbortSignal) => Promise<OpcionReporte[]>;
}

type Scope = { transitOfficeId?: string };

const conScope = (filtro: FiltroReportePortal, scope?: Scope): FiltroReportePortal =>
  scope?.transitOfficeId ? { ...filtro, transitOfficeId: scope.transitOfficeId } : filtro;

function listarEn(base: string, filtro: FiltroReportePortal, signal?: AbortSignal) {
  return apiFetch<PaginaReportePortal>(base, { query: { ...filtro }, signal });
}

function exportarEn(base: string, filtro: FiltroReportePortal): Promise<void> {
  const sinPaginar = { ...filtro };
  delete sinPaginar.page;
  delete sinPaginar.pageSize;
  return downloadFile(`${base}/export`, {
    query: sinPaginar,
    fallbackFilename: "reporte-portal-persona-natural.xlsx",
  });
}

/** Hub del OT: el `ot_admin` no envía organismo; el SuperAdmin envía el del hub (`scope`). */
export function crearReporteOtApi(
  scope: Scope | undefined,
  tipos: (signal?: AbortSignal) => Promise<OpcionReporte[]>,
): ReportePortalApi {
  return {
    listar: (filtro, signal) => listarEn(OT_PORTAL_REPORTES_BASE, conScope(filtro, scope), signal),
    exportar: (filtro) => exportarEn(OT_PORTAL_REPORTES_BASE, conScope(filtro, scope)),
    tipos,
  };
}

/** Plataforma (SuperAdmin): el organismo, si se filtra, viaja en `transitOfficeId` dentro del filtro. */
export function crearReportePlataformaApi(
  tipos: (signal?: AbortSignal) => Promise<OpcionReporte[]>,
  organismos: (signal?: AbortSignal) => Promise<OpcionReporte[]>,
): ReportePortalApi {
  return {
    listar: (filtro, signal) => listarEn(PLATAFORMA_PORTAL_REPORTES_BASE, filtro, signal),
    exportar: (filtro) => exportarEn(PLATAFORMA_PORTAL_REPORTES_BASE, filtro),
    tipos,
    organismos,
  };
}
