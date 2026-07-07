// Cliente tipado de mandatarios (firmantes de mandato) por OT (ADR-0023, RF22–RF34).
// Módulo Admin OT (SuperAdmin u ot_admin). Endpoints acotados por transitOfficeId en la ruta.
// El número de documento es PII (Ley 1581): se recibe solo para precargar el formulario.
import { apiFetch } from "./client";

/** Mandatario con sus compañías asignadas (RF27). */
export interface MandateSigner {
  id: string;
  transitOfficeId: string;
  fullName: string;
  documentNumber: string;
  integrityHash: string;
  registeredAt: string;
  isActive: boolean;
  companyTenantIds: string[];
}

/**
 * Compañía del OT con su mandatario resuelto (RF34, vista consolidada + multiselect).
 * `assignedSigner*` nulo = compañía sin mandatario (RF26): se advierte, no se bloquea.
 */
export interface OtCompany {
  companyTenantId: string;
  legalName: string;
  isActive: boolean;
  isEnabled: boolean;
  assignedSignerId: string | null;
  assignedSignerName: string | null;
  assignedSignerHash: string | null;
}

export interface MandateSignerInput {
  fullName: string;
  documentNumber: string;
  companyTenantIds: string[];
}

export interface MandateSignerSaved {
  id: string;
  integrityHash: string;
}

function base(transitOfficeId: string): string {
  return `/api/v1/admin/transit-offices/${transitOfficeId}/mandate-signers`;
}

/** GET — mandatarios activos del OT (RF27). */
export async function fetchMandateSigners(
  transitOfficeId: string,
  signal?: AbortSignal,
): Promise<MandateSigner[]> {
  const result = await apiFetch<{ data: MandateSigner[] }>(base(transitOfficeId), { signal });
  return result.data;
}

/** GET /companies — compañías del OT con su mandatario asignado (RF34 + multiselect). */
export async function fetchOtCompanies(
  transitOfficeId: string,
  signal?: AbortSignal,
): Promise<OtCompany[]> {
  const result = await apiFetch<{ data: OtCompany[] }>(`${base(transitOfficeId)}/companies`, { signal });
  return result.data;
}

/** POST — alta de mandatario (RF22). Lanza ApiValidationError en 422 (exclusividad/RF33). */
export function createMandateSigner(
  transitOfficeId: string,
  body: MandateSignerInput,
): Promise<MandateSignerSaved> {
  return apiFetch<MandateSignerSaved>(base(transitOfficeId), { method: "POST", body });
}

/** PUT /{signerId} — edición (RF23, regenera la huella). */
export function updateMandateSigner(
  transitOfficeId: string,
  mandateSignerId: string,
  body: MandateSignerInput,
): Promise<MandateSignerSaved> {
  return apiFetch<MandateSignerSaved>(`${base(transitOfficeId)}/${mandateSignerId}`, {
    method: "PUT",
    body,
  });
}

/** POST /{signerId}/inactivate — baja lógica que libera compañías (RF24). */
export function inactivateMandateSigner(
  transitOfficeId: string,
  mandateSignerId: string,
): Promise<void> {
  return apiFetch<void>(`${base(transitOfficeId)}/${mandateSignerId}/inactivate`, {
    method: "POST",
  });
}
