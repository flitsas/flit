// Cliente tipado del módulo LOG QX (HU #10795, Feature #10792). Consume el endpoint de
// solo lectura GET /api/v1/admin/log-qx (HU #10793 + gate logqx.read/enmascarado HU #10794).
import { apiFetch } from "./client";

const base = "/api/v1/admin/log-qx";

/** Estado final de una radicación Quipux (espeja el enum del contrato `LogQxEntry.status`). */
export type LogQxStatus = "pendiente" | "registrado" | "aprobado" | "rechazado" | "fallido";

/**
 * Un evento de la línea de tiempo (`LogQxEvent`). `detail` es el jsonb SANITIZADO y
 * ENMASCARADO tal cual lo devuelve el backend, o `null` = "sin payload disponible".
 */
export interface LogQxEvent {
  stage: string;
  outcome: string;
  /** Objeto JSON arbitrario ya sanitizado/enmascarado, o null si el evento no tiene detalle. */
  detail: Record<string, unknown> | null;
  /** Duración de la llamada HTTP en ms; null en eventos previos a la instrumentación. */
  durationMs: number | null;
  /** Worker que originó el evento (quipux_register / quipux_status_poll). */
  origin: string | null;
  responseCode: number | null;
  correlationId: string | null;
  occurredAt: string;
}

/** Una radicación con su línea de tiempo (`LogQxEntry`). */
export interface LogQxEntry {
  id: string;
  procedureInstanceId: string;
  referenceNumber: string;
  procedureTypeName: string;
  clientTenantName: string;
  plate: string | null;
  documentName: string;
  divipoCode: string | null;
  status: LogQxStatus;
  attempts: number;
  pollCount: number;
  qxRegisterCode: number | null;
  qxProcedureCode: number | null;
  rejectionReason: string | null;
  createdAt: string;
  registeredAt: string | null;
  lastPolledAt: string | null;
  completedAt: string | null;
  updatedAt: string | null;
  events: LogQxEvent[];
}

/** Página del LOG QX con eco de paginación (`LogQxPage`). */
export interface LogQxPage {
  data: LogQxEntry[];
  totalCount: number;
  page: number;
  pageSize: number;
}

/** Filtros de búsqueda: los tres ejes son excluyentes-opcionales (placa | trámite | radicado). */
export interface LogQxSearchParams {
  placa?: string;
  instanceId?: string;
  radicado?: string;
  page?: number;
  pageSize?: number;
}

/**
 * GET /api/v1/admin/log-qx — busca radicaciones por placa, id de trámite o radicado y
 * devuelve su línea de tiempo, paginada. Requiere el permiso `logqx.read` (SuperAdmin bypassa).
 */
export function fetchLogQx(
  params: LogQxSearchParams = {},
  signal?: AbortSignal,
): Promise<LogQxPage> {
  return apiFetch<LogQxPage>(base, {
    query: {
      placa: params.placa,
      instanceId: params.instanceId,
      radicado: params.radicado,
      page: params.page,
      pageSize: params.pageSize,
    },
    signal,
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// Rediseño del módulo (Feature #11784). La búsqueda de arriba queda para la
// correlación por trámite; lo que sigue alimenta la bandeja y la trazabilidad.
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Estados de la BANDEJA. No espejan uno a uno `LogQxStatus`: `sin_radicar` es un
 * trámite elegible que nunca se encoló, y `registrado` se parte en `radicado`
 * (aún sin sondear) y `en_tramite` (ya sondeando) — una espera al worker, la
 * otra a la secretaría.
 */
export type LogQxBandejaEstado =
  | "sin_radicar"
  | "pendiente"
  | "radicado"
  | "en_tramite"
  | "aprobado"
  | "rechazado"
  | "fallido";

/** Una fila de la bandeja: UN TRÁMITE, no una radicación. */
export interface LogQxBandejaEntry {
  procedureInstanceId: string;
  referenceNumber: string;
  plate: string | null;
  procedureTypeName: string;
  estado: LogQxBandejaEstado;
  clientTenantName: string;
  transitOfficeName: string;
  divipoCode: string | null;
  /** Nombre del documento en Quipux; null si aún no se radicó. Quipux no emite radicado. */
  documentoQx: string | null;
  /** Radicación más reciente; null en `sin_radicar`. */
  submissionId: string | null;
  /** Cuántas radicaciones acumuló el trámite. */
  intentos: number;
  attempts: number;
  pollCount: number;
  qxRegisterCode: number | null;
  qxProcedureCode: number | null;
  rejectionReason: string | null;
  ultimaActividad: string | null;
  esperandoDesde: string | null;
  /** Horas de espera, calculadas en servidor. Null en los estados terminales. */
  horasEsperando: number | null;
  submissionCreatedAt: string | null;
}

export interface LogQxBandejaContador {
  estado: LogQxBandejaEstado;
  total: number;
}

export interface LogQxBandejaPage {
  data: LogQxBandejaEntry[];
  totalCount: number;
  page: number;
  pageSize: number;
  contadores: LogQxBandejaContador[];
}

/** Filtros de la bandeja: todos COMBINABLES entre sí, a diferencia de la búsqueda vieja. */
export interface LogQxBandejaParams {
  desde?: string;
  hasta?: string;
  placa?: string;
  instanceId?: string;
  referencia?: string;
  documento?: string;
  estado?: LogQxBandejaEstado | "";
  transitOfficeId?: string;
  tenantId?: string;
  procedureTypeId?: string;
  page?: number;
  pageSize?: number;
}

/**
 * GET /api/v1/admin/log-qx/bandeja — entrada del módulo. Sin filtros devuelve el
 * periodo por defecto, así que la pantalla carga con datos sin buscar nada.
 */
export function fetchLogQxBandeja(
  params: LogQxBandejaParams = {},
  signal?: AbortSignal,
): Promise<LogQxBandejaPage> {
  return apiFetch<LogQxBandejaPage>(`${base}/bandeja`, { query: { ...params }, signal });
}

/** Una radicación hermana del mismo trámite, para la tira de intentos. */
export interface LogQxHermana {
  id: string;
  intento: number;
  status: LogQxStatus;
  createdAt: string;
}

/** Cabecera de la radicación en la pantalla de trazabilidad. */
export interface LogQxRadicacion {
  id: string;
  procedureInstanceId: string;
  referenceNumber: string;
  plate: string | null;
  procedureTypeName: string;
  clientTenantName: string;
  transitOfficeName: string;
  divipoCode: string | null;
  documentoQx: string;
  status: LogQxStatus;
  attempts: number;
  pollCount: number;
  qxRegisterCode: number | null;
  qxProcedureCode: number | null;
  rejectionReason: string | null;
  createdAt: string;
  registeredAt: string | null;
  lastPolledAt: string | null;
  completedAt: string | null;
  updatedAt: string | null;
  intento: number;
  totalIntentos: number;
  hermanas: LogQxHermana[];
}

/**
 * Una entrada de la línea de tiempo. `sondeo` es una racha de consultas sin
 * novedad ya colapsada por el servidor: `consultas` dice cuántas, y `occurredAt`
 * y `hasta` delimitan la ventana. En un `hito`, esos tres van en null.
 */
export interface LogQxHito {
  tipo: "hito" | "sondeo";
  stage: string;
  outcome: string;
  occurredAt: string;
  hasta: string | null;
  durationMs: number | null;
  codigo: number | null;
  estadoTramite: number | null;
  mensaje: string | null;
  correlationId: string | null;
  consultas: number | null;
  duracionMediaMs: number | null;
}

export interface LogQxHitosResult {
  radicacion: LogQxRadicacion;
  hitos: LogQxHito[];
}

/** GET /api/v1/admin/log-qx/{submissionId}/hitos — con el sondeo ya agrupado. */
export function fetchLogQxHitos(
  submissionId: string,
  signal?: AbortSignal,
): Promise<LogQxHitosResult> {
  return apiFetch<LogQxHitosResult>(`${base}/${submissionId}/hitos`, { signal });
}

export interface LogQxEventosParams {
  ocultarSinNovedad?: boolean;
  soloErrores?: boolean;
  page?: number;
  pageSize?: number;
}

/**
 * Página del log completo. `ocultosSinNovedad` es cuántos sondeos se dejaron
 * fuera: sin ese número, ver 5 filas de una radicación de 1.065 eventos parece
 * una pérdida de datos.
 */
export interface LogQxEventosPage {
  data: LogQxEvent[];
  totalCount: number;
  page: number;
  pageSize: number;
  ocultosSinNovedad: number;
  totalEventos: number;
}

/** GET /api/v1/admin/log-qx/{submissionId}/eventos — filtrado y paginado en servidor. */
export function fetchLogQxEventos(
  submissionId: string,
  params: LogQxEventosParams = {},
  signal?: AbortSignal,
): Promise<LogQxEventosPage> {
  return apiFetch<LogQxEventosPage>(`${base}/${submissionId}/eventos`, {
    query: { ...params },
    signal,
  });
}
