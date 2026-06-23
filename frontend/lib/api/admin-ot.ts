// Cliente tipado de la API admin OT (HU #10215, #10217, #10218).
import { apiFetch } from "./client";
import type {
  OtClientProcedurePagedResult,
  OtClientProceduresParams,
  OtProfile,
  UpdateOtProfileRequest,
} from "./types-ot";

const base = "/api/v1/admin/ot";

/** GET /profile — perfil OT del tenant autenticado (AC5: fuente de verdad del modo). */
export function fetchOtProfile(signal?: AbortSignal): Promise<OtProfile> {
  return apiFetch<OtProfile>(`${base}/profile`, { signal });
}

/** PATCH /profile — actualiza modo Dashboard/QX (AC2). */
export function updateOtProfile(body: UpdateOtProfileRequest): Promise<OtProfile> {
  return apiFetch<OtProfile>(`${base}/profile`, { method: "PATCH", body });
}

/** GET /client-procedures — trámites de clientes con grant vigente (HU #10217). */
export function fetchOtClientProcedures(
  params: OtClientProceduresParams = {},
  signal?: AbortSignal,
): Promise<OtClientProcedurePagedResult> {
  return apiFetch<OtClientProcedurePagedResult>(`${base}/client-procedures`, {
    query: { ...params },
    signal,
  });
}
