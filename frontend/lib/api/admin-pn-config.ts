// HU #14074/#14075 (Feature #14070, Épica #14021) — cliente tipado de la CONFIGURACIÓN del portal de Persona
// Natural en Plataforma (HU #14072, solo SuperAdmin). Contrato real leído de
// `AdminPlataformaPersonaNaturalEndpoints.Configuracion.cs`:
//   GET/PUT /api/v1/admin/plataforma/persona-natural/configuracion/parametros  (rowVersion obligatorio; 409 con `actual`)
//   GET     …/organismos?soloHabilitados      PUT …/organismos/{id} { habilitado }
//   GET     …/tipos                           PUT …/tipos/{tipoTramiteId} { habilitado }  (422 `tipo_no_elegible`)
// NO hay datos bancarios ni de pago (decisión del PO: viven en otra épica).
import { apiFetch } from "./client";
import { PN_BASE } from "./admin-pn-tarifas";

/** `ParametrosPn`: cada valor nulo = rige el predeterminado del BFF. `rowVersion` es el control de concurrencia. */
export interface ParametrosPn {
  sesionVigenciaMinutos: number | null;
  borradorVigenciaDias: number | null;
  topeConsultasCiudadanoDia: number | null;
  topeConsultasGlobalDia: number | null;
  captchaActivo: boolean | null;
  soporteCorreo: string | null;
  soporteTelefono: string | null;
  soporteHorario: string | null;
  rowVersion: number;
  actualizadoEn: string | null;
  actualizadoPor: string | null;
}

/** Lo que se envía al guardar: los mismos campos editables más la versión que se leyó. */
export type GuardarParametrosPn = Omit<ParametrosPn, "rowVersion" | "actualizadoEn" | "actualizadoPor">;

/** Rangos permitidos (los mismos CHECK del backend, `ConfiguracionPnHandlers`). */
export const RANGOS_PARAMETROS = {
  sesionVigenciaMinutos: { min: 15, max: 1440, unidad: "minutos" },
  borradorVigenciaDias: { min: 1, max: 90, unidad: "días" },
  topeConsultasCiudadanoDia: { min: 1, max: 100, unidad: "consultas" },
  topeConsultasGlobalDia: { min: 1, max: 100_000, unidad: "consultas" },
} as const;
export const SOPORTE_CORREO_MAX = 254;
export const SOPORTE_TELEFONO_MAX = 30;
export const SOPORTE_HORARIO_MAX = 120;

export interface OrganismoPn {
  id: string;
  codigo: string;
  nombre: string;
  municipio: string;
  departamento: string;
  habilitado: boolean;
  /** Tipos habilitados y elegibles con derechos de tránsito y servicio digital vigentes en el organismo. */
  tiposConTarifaVigente: number;
  /** `false` = el organismo ya no está activo en FLIT (sigue en la lista solo para poder apagarlo). Ausente = activo. */
  activoEnFlit?: boolean;
}

export interface TipoPn {
  id: string;
  codigo: string;
  nombre: string;
  familia: string;
  elegible: boolean;
  motivo: string;
  habilitado: boolean;
  /** Elegible y habilitado: lo que el portal ofrece de verdad. */
  ofrecible: boolean;
  organismosConTarifaVigente: number;
  /** `false` = el trámite ya no está activo en FLIT (sigue en la lista solo para poder apagarlo). Ausente = activo. */
  activoEnFlit?: boolean;
}

export function fetchParametrosPn(signal?: AbortSignal): Promise<ParametrosPn> {
  return apiFetch<ParametrosPn>(`${PN_BASE}/configuracion/parametros`, { signal });
}

export function guardarParametrosPn(body: GuardarParametrosPn, rowVersion: number): Promise<ParametrosPn> {
  return apiFetch<ParametrosPn>(`${PN_BASE}/configuracion/parametros`, { method: "PUT", body: { ...body, rowVersion } });
}

export function fetchOrganismosPn(signal?: AbortSignal): Promise<OrganismoPn[]> {
  return apiFetch<OrganismoPn[]>(`${PN_BASE}/organismos`, { signal });
}

export function establecerOrganismoPn(id: string, habilitado: boolean): Promise<void> {
  return apiFetch<void>(`${PN_BASE}/organismos/${id}`, { method: "PUT", body: { habilitado } });
}

export function fetchTiposPn(signal?: AbortSignal): Promise<TipoPn[]> {
  return apiFetch<TipoPn[]>(`${PN_BASE}/tipos`, { signal });
}

export function establecerTipoPn(tipoTramiteId: string, habilitado: boolean): Promise<void> {
  return apiFetch<void>(`${PN_BASE}/tipos/${tipoTramiteId}`, { method: "PUT", body: { habilitado } });
}
