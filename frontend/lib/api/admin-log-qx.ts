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
