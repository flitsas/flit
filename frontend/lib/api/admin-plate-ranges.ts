// Cliente tipado de la consola de preasignación de placa (Feature #10587, HU #10651–#10656).
import { apiFetch } from "./client";

const base = "/api/v1/admin/plate-ranges";

export type PlateState =
  | "disponible"
  | "preasignada"
  | "utilizada"
  | "bloqueada"
  | "revocada";

export interface PlateRangeSummary {
  id: string;
  tenantId: string;
  transitOfficeId: string;
  prefix: string;
  rangeFrom: number;
  rangeTo: number;
  editableUntil: string;
  totalPlates: number;
  availablePlates: number;
}

export interface PlateDetail {
  id: string;
  plateRangeId: string;
  tenantId: string;
  transitOfficeId: string;
  plate: string;
  state: PlateState;
  procedureInstanceId: string | null;
}

export interface AssignPlateRangeRequest {
  companyTenantId: string;
  prefix: string;
  rangeFrom: number;
  rangeTo: number;
  transitOfficeId?: string;
}

export interface EditPlateRangeRequest {
  prefix: string;
  rangeFrom: number;
  rangeTo: number;
}

export interface PlateScope {
  transitOfficeId?: string;
}

function scopeQuery(companyTenantId: string, scope?: PlateScope, extra?: Record<string, string>) {
  return {
    companyTenantId,
    ...(scope?.transitOfficeId ? { transitOfficeId: scope.transitOfficeId } : {}),
    ...(extra ?? {}),
  };
}

export function listPlateRanges(
  companyTenantId: string,
  scope?: PlateScope,
  signal?: AbortSignal,
): Promise<PlateRangeSummary[]> {
  return apiFetch<PlateRangeSummary[]>(base, { query: scopeQuery(companyTenantId, scope), signal });
}

export function listPlateDetails(
  companyTenantId: string,
  opts?: { state?: PlateState; scope?: PlateScope; signal?: AbortSignal },
): Promise<PlateDetail[]> {
  return apiFetch<PlateDetail[]>(`${base}/plates`, {
    query: scopeQuery(companyTenantId, opts?.scope, opts?.state ? { state: opts.state } : undefined),
    signal: opts?.signal,
  });
}

export function assignPlateRange(
  body: AssignPlateRangeRequest,
): Promise<{ rangeId: string; platesCreated: number }> {
  return apiFetch(base, { method: "POST", body });
}

export function editPlateRange(
  rangeId: string,
  body: EditPlateRangeRequest,
): Promise<{ rangeId: string; platesCreated: number }> {
  return apiFetch(`${base}/${rangeId}`, { method: "PUT", body });
}

export function blockPlate(plateId: string): Promise<void> {
  return apiFetch(`${base}/plates/${plateId}/block`, { method: "POST" });
}

export function unblockPlate(plateId: string): Promise<void> {
  return apiFetch(`${base}/plates/${plateId}/unblock`, { method: "POST" });
}

export function revokePlate(plateId: string): Promise<void> {
  return apiFetch(`${base}/plates/${plateId}/revoke`, { method: "POST" });
}

export function assignPlateToProcedure(instanceId: string, plate: string): Promise<unknown> {
  return apiFetch(`${base}/procedures/${instanceId}/assign-plate`, { method: "POST", body: { plate } });
}

export function revokeProcedurePlate(instanceId: string, reason: string): Promise<unknown> {
  return apiFetch(`${base}/procedures/${instanceId}/revoke`, { method: "POST", body: { reason } });
}

/** Placas DISPONIBLES para la compañía en el OT elegido (company-facing, para el wizard). */
export function listAvailablePlatesForCompany(
  transitOfficeId: string,
  signal?: AbortSignal,
): Promise<PlateDetail[]> {
  return apiFetch<PlateDetail[]>("/api/v1/tramites/plate-preassign/available", {
    query: { transitOfficeId },
    signal,
  });
}

export const PLATE_STATE_LABELS: Record<PlateState, string> = {
  disponible: "Disponible",
  preasignada: "Preasignada",
  utilizada: "Utilizada",
  bloqueada: "Bloqueada",
  revocada: "Revocada",
};
