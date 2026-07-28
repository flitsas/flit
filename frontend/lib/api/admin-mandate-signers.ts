// Cliente tipado de mandatarios (firmantes de mandato) por OT (ADR-0023, ampliado por ADR-0036).
// Módulo Admin OT (SuperAdmin u ot_admin). Endpoints acotados por transitOfficeId en la ruta.
// El número de documento y el correo son PII (Ley 1581): se reciben solo para precargar el formulario.
import { apiFetch } from "./client";

/** Un mandatario asignado a una compañía (ADR-0036: multiplicidad ⇒ una compañía puede tener varios). */
export interface AssignedSigner {
  mandateSignerId: string;
  fullName: string;
  integrityHash: string;
}

/** Mandatario con sus compañías asignadas (RF27, ampliado por ADR-0036). */
export interface MandateSigner {
  id: string;
  transitOfficeId: string;
  fullName: string;
  /** Tipo de documento (ADR-0036). Por defecto "CC". */
  documentType: string;
  documentNumber: string;
  integrityHash: string;
  /** Correo para la validación de identidad (ADR-0036, HU #10911). PII. `null` si no se capturó. */
  email: string | null;
  /** Cuenta de usuario de OT del mandatario (ADR-0036 §D9): cotejo del firmante al aprobar. */
  userId: string | null;
  /** Validación de identidad admin vigente vinculada (ADR-0034/0036): `null` = sin validar. */
  identityValidationRef: string | null;
  /**
   * Estado de la validación de identidad (HU #10994): `"valid"` (aprobada y vigente),
   * `"expired"` (vencida/rechazada ⇒ se puede renovar), `"pending"` (enviada/en proceso) o `"none"`.
   */
  identityStatus: "valid" | "expired" | "pending" | "none";
  /** Firma del baúl vinculada (ADR-0025), si está resuelta. */
  signatureVaultId: string | null;
  registeredAt: string;
  isActive: boolean;
  companyTenantIds: string[];
}

/**
 * Compañía del OT con sus mandatarios resueltos (RF34, ADR-0036). `assignedSigners` vacío = compañía
 * sin mandatario (RF26): se advierte, no se bloquea. Con la multiplicidad puede traer varios.
 */
export interface OtCompany {
  companyTenantId: string;
  legalName: string;
  isActive: boolean;
  isEnabled: boolean;
  assignedSigners: AssignedSigner[];
}

export interface MandateSignerInput {
  fullName: string;
  documentType: string;
  documentNumber: string;
  /** Correo para la validación de identidad; `null` si no se captura. */
  email: string | null;
  /** Cuenta de usuario de OT a vincular (§D9); `null` si no se asigna. */
  userId: string | null;
  companyTenantIds: string[];
}

export interface MandateSignerSaved {
  id: string;
  integrityHash: string;
}

/** Resultado de iniciar/reenviar la validación de identidad del mandatario (ADR-0036, HU #10911). */
export interface MandateSignerIdentityResult {
  id: string;
  status: string;
  captureUrl: string | null;
  validUntil: string | null;
  reused: boolean;
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

/** GET /companies — compañías del OT con sus mandatarios asignados (RF34 + multiselect). */
export async function fetchOtCompanies(
  transitOfficeId: string,
  signal?: AbortSignal,
): Promise<OtCompany[]> {
  const result = await apiFetch<{ data: OtCompany[] }>(`${base(transitOfficeId)}/companies`, { signal });
  return result.data;
}

/** POST — alta de mandatario (RF22). Lanza ApiValidationError en 422 (RF33). */
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

/** POST /{signerId}/reactivate — reactiva un mandatario inactivado (sin compañías). */
export function reactivateMandateSigner(
  transitOfficeId: string,
  mandateSignerId: string,
): Promise<void> {
  return apiFetch<void>(`${base(transitOfficeId)}/${mandateSignerId}/reactivate`, {
    method: "POST",
  });
}

/**
 * POST /{signerId}/identity/send — inicia la validación de identidad del mandatario por correo
 * (ADR-0036, HU #10911). Lanza ApiValidationError en 422 (email_requerido / ot_sin_alta); el proveedor
 * no disponible es 503 y su error definitivo 502.
 */
export function sendMandateSignerIdentity(
  transitOfficeId: string,
  mandateSignerId: string,
): Promise<MandateSignerIdentityResult> {
  return apiFetch<MandateSignerIdentityResult>(
    `${base(transitOfficeId)}/${mandateSignerId}/identity/send`,
    { method: "POST" },
  );
}

/** POST /{signerId}/identity/resend — reenvía (respeta la vigencia: no reenvía si ya está aprobada). */
export function resendMandateSignerIdentity(
  transitOfficeId: string,
  mandateSignerId: string,
): Promise<MandateSignerIdentityResult> {
  return apiFetch<MandateSignerIdentityResult>(
    `${base(transitOfficeId)}/${mandateSignerId}/identity/resend`,
    { method: "POST" },
  );
}
