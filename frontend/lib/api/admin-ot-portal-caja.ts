// HU #14455 / #14462 (Feature #14453, Épica #14210) — cliente de la caja del portal desde el hub del OT.
// Contrato: contracts/openapi/core-api.v1.yaml → `CajaUsuario`, `CajaCobro`, `CajaCobroLista`.
//   /api/v1/admin/ot/portal-caja/users, /invitations/{id}/resend y /cobros.
// El `ot_admin` trabaja con el organismo de su token; el SuperAdmin envía `transitOfficeId`.
import { apiFetch } from "./client";

export const OT_PORTAL_CAJA_BASE = "/api/v1/admin/ot/portal-caja";

export type CajaUsuarioEstado = "invited" | "active" | "suspended";

export interface CajaUsuario {
  userId: string | null;
  invitationId: string | null;
  fullName: string;
  email: string;
  status: CajaUsuarioEstado;
  createdAt: string | null;
}

export interface CajaInvitacion {
  email: string;
  fullName: string;
}

export interface CajaCobro {
  cobroId: string;
  codigo: string;
  valorCop: number;
  medio: string;
  medioDetalle?: string | null;
  pagadoEn?: string | null;
  pagadoPorNombre?: string | null;
  estado: "pagado" | "anulado";
  anulable: boolean;
  motivoNoAnulable?: string | null;
  anuladoEn?: string | null;
  anuladoPorNombre?: string | null;
  anulacionMotivo?: string | null;
  rowVersion: number;
}

export interface CajaCobroLista {
  items: CajaCobro[];
  total: number;
  totalDiaCop: number;
}

export interface CajaCobrosFiltro {
  /** AAAA-MM-DD en hora de Colombia; sin él, hoy. */
  date?: string;
  page: number;
  pageSize: number;
}

export interface OtCajaScope {
  /** Solo el SuperAdmin lo envía; el `ot_admin` lo omite y el servidor usa su token. */
  transitOfficeId?: string;
}

export const MOTIVO_ANULACION_MIN = 10;
export const MOTIVO_ANULACION_MAX = 300;

/** Cliente con el ámbito ya resuelto. Debe ser estable entre renders. */
export interface CajaApi {
  listarUsuarios: (signal?: AbortSignal) => Promise<CajaUsuario[]>;
  invitar: (body: CajaInvitacion) => Promise<unknown>;
  suspender: (userId: string) => Promise<unknown>;
  reactivar: (userId: string) => Promise<unknown>;
  reenviar: (invitationId: string) => Promise<unknown>;
  listarCobros: (filtro: CajaCobrosFiltro, signal?: AbortSignal) => Promise<CajaCobroLista>;
  anular: (cobro: Pick<CajaCobro, "cobroId" | "rowVersion">, motivo: string) => Promise<unknown>;
}

export function crearCajaApiOt(scope?: OtCajaScope): CajaApi {
  const query: Record<string, string> = scope?.transitOfficeId ? { transitOfficeId: scope.transitOfficeId } : {};
  const base = OT_PORTAL_CAJA_BASE;
  return {
    listarUsuarios: (signal) => apiFetch<CajaUsuario[]>(`${base}/users`, { query, signal }),
    invitar: (body) =>
      apiFetch<unknown>(`${base}/users`, {
        method: "POST",
        query,
        body: { email: body.email.trim(), fullName: body.fullName.trim() },
      }),
    suspender: (id) => apiFetch<unknown>(`${base}/users/${id}/suspend`, { method: "POST", query }),
    reactivar: (id) => apiFetch<unknown>(`${base}/users/${id}/suspend`, { method: "DELETE", query }),
    reenviar: (id) => apiFetch<unknown>(`${base}/invitations/${id}/resend`, { method: "POST", query }),
    listarCobros: (f, signal) =>
      apiFetch<CajaCobroLista>(`${base}/cobros`, {
        query: { ...query, date: f.date, page: f.page, pageSize: f.pageSize },
        signal,
      }),
    anular: (c, motivo) =>
      apiFetch<unknown>(`${base}/cobros/${c.cobroId}/anular`, {
        method: "POST",
        query,
        body: { reason: motivo.trim(), rowVersion: c.rowVersion },
      }),
  };
}
