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
  /**
   * HU #11060 — hasta cuándo es válida la identidad. Solo viene con `identityStatus: "valid"`; `null`
   * en el resto de estados y también en una aprobación sin caducidad registrada.
   */
  identityValidUntil?: string | null;
  /** Firma del baúl vinculada (ADR-0025), si está resuelta. */
  signatureVaultId: string | null;
  registeredAt: string;
  isActive: boolean;
  companyTenantIds: string[];
  /**
   * HU #11201 — organismos donde aplica el mandatario. `transitOfficeId` es solo el primario
   * (deprecado): esta lista es la que dice dónde puede firmar.
   */
  transitOfficeIds?: string[];
  /** Subconjunto de los anteriores donde el mandatario firma a mano. */
  physicalSignatureOfficeIds?: string[];
  /** Empresas representadas por organismo; vacío para un organismo ⇒ aplica a todas allí. */
  officeCompanies?: MandateSignerOfficeCompanies[];
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
  /**
   * Desenlace de la validación de identidad disparada por el alta (HU #11000): `"sent"` (correo
   * enviado), `"reused"` (la persona ya tenía identidad vigente y se apalancó), `"failed"` (el
   * proveedor falló; el mandatario quedó creado) o `"notattempted"` (se registró sin correo).
   * Solo viaja en el POST de alta.
   */
  identity?: "sent" | "reused" | "failed" | "notattempted";
}

/** Resultado de iniciar/reenviar la validación de identidad del mandatario (ADR-0036, HU #10911). */
export interface MandateSignerIdentityResult {
  id: string;
  status: string;
  captureUrl: string | null;
  validUntil: string | null;
  reused: boolean;
  /** HU #11028 — `"mock"` cuando la validación fue simulada en un ambiente de prueba. */
  provider?: string;
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

/**
 * GET — igual que `fetchMandateSigners` pero conservando si el ambiente permite SIMULAR validaciones
 * de identidad (HU #11028). La consola solo ofrece esa acción cuando el backend la habilita.
 */
export async function fetchMandateSignersWithFlags(
  transitOfficeId: string,
  signal?: AbortSignal,
): Promise<{ signers: MandateSigner[]; mockIdentityEnabled: boolean }> {
  const result = await apiFetch<{ data: MandateSigner[]; mockIdentityEnabled?: boolean }>(
    base(transitOfficeId),
    { signal },
  );
  return { signers: result.data, mockIdentityEnabled: result.mockIdentityEnabled === true };
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

/**
 * POST /{signerId}/identity/link — vincula al mandatario una validación que la PERSONA ya hizo y sigue
 * vigente (HU #11028). No envía correo ni crea nada: lanza ApiError 409 `sin_identidad_vigente` cuando
 * esa persona no tiene ninguna identidad aprobada y vigente.
 */
export function linkMandateSignerIdentity(
  transitOfficeId: string,
  mandateSignerId: string,
): Promise<MandateSignerIdentityResult> {
  return apiFetch<MandateSignerIdentityResult>(
    `${base(transitOfficeId)}/${mandateSignerId}/identity/link`,
    { method: "POST" },
  );
}

/**
 * POST /{signerId}/identity/mock — SIMULA una validación aprobada (HU #11028). Solo disponible en
 * ambientes de prueba: con la simulación deshabilitada el backend responde 403 `simulacion_deshabilitada`.
 * La validación queda marcada como simulada (`provider: "mock"`), nunca se confunde con una real.
 */
export function mockMandateSignerIdentity(
  transitOfficeId: string,
  mandateSignerId: string,
): Promise<MandateSignerIdentityResult> {
  return apiFetch<MandateSignerIdentityResult>(
    `${base(transitOfficeId)}/${mandateSignerId}/identity/mock`,
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

// ── HU #11202 — mandatarios desde el configurador de la COMPAÑÍA ──────────────
// Vista inversa: la empresa registra a la persona y marca en cuáles de SUS organismos aplica, en vez
// de que cada organismo elija compañías. Mismos objetos de dominio; cambia la ruta y quién manda.

/** Organismo de tránsito habilitado para la compañía (opción del multiselect). */
export interface CompanyTransitOfficeOption {
  transitOfficeId: string;
  code: string;
  name: string;
}

/** Datos que la compañía captura de un mandatario. */
export interface CompanyMandateSignerInput {
  fullName: string;
  documentType: string;
  documentNumber: string;
  email: string | null;
  /** Organismos donde aplica. Al editar, REEMPLAZA a los anteriores: quitar uno lo retira. */
  transitOfficeIds: string[];
  /**
   * Subconjunto de los anteriores donde el mandatario firma A MANO: el contrato deja la línea con sus
   * datos debajo y no estampa firma del baúl ni sello de identidad.
   */
  physicalSignatureOfficeIds?: string[];
  /**
   * Firma del baúl elegida para el mandatario. `null` ⇒ el trámite la resuelve por documento, que es
   * el comportamiento previo.
   */
  signatureVaultId?: string | null;
  /**
   * Empresas representadas por organismo. Omitir la entrada de un organismo ⇒ el mandatario aplica a
   * todas las empresas allí.
   */
  officeCompanies?: MandateSignerOfficeCompanies[];
}

/** Empresa representada de la compañía: las que se dan de alta en el formulario del representante. */
export interface RepresentedCompanyOption {
  id: string;
  /** NIT. Es lo que distingue dos empresas con la misma razón social. */
  documentNumber: string;
  name: string;
}

/**
 * Empresas representadas para las que el mandatario firma en un organismo. Lista vacía ⇒ aplica a
 * TODAS las de ese organismo, que es como se comportan los mandatarios que ya existen.
 */
export interface MandateSignerOfficeCompanies {
  transitOfficeId: string;
  representedCompanyIds: string[];
}

/** GET — empresas representadas de la compañía. Ya vienen únicas por NIT. */
export async function fetchRepresentedCompanies(
  tenantId: string,
  signal?: AbortSignal,
): Promise<RepresentedCompanyOption[]> {
  const r = await apiFetch<{ items: RepresentedCompanyOption[] }>(
    `${companyBase(tenantId)}/represented-companies`,
    { signal },
  );
  return r?.items ?? [];
}

/** Desenlace de una acción de identidad sobre el mandatario. */
export interface MandateSignerIdentityResult {
  estado: string;
  reutilizada: boolean;
}

/**
 * Acciones de identidad del mandatario desde el configurador de la COMPAÑÍA.
 *
 * Existían solo bajo `/transit-offices/...` y ningún componente las llamaba: la empresa registraba a
 * su mandatario pero no tenía forma de pedirle la validación ni de vincular la que ya tuviera.
 *
 * - `send` inicia la validación (el proveedor manda el enlace de captura por correo);
 * - `resend` reenvía respetando la vigencia (no reenvía si ya hay aprobada y vigente);
 * - `link` vincula una identidad que esa persona YA validó, sin mandar correo (409 si no tiene).
 */
export function mandateSignerIdentityAction(
  tenantId: string,
  mandateSignerId: string,
  accion: "send" | "resend" | "link",
): Promise<MandateSignerIdentityResult> {
  return apiFetch<MandateSignerIdentityResult>(
    `${companyBase(tenantId)}/${mandateSignerId}/identity/${accion}`,
    { method: "POST" },
  );
}

function companyBase(tenantId: string): string {
  return `/api/v1/admin/companies/${tenantId}/mandate-signers`;
}

/** GET — mandatarios de la compañía, con sus organismos. */
export async function fetchCompanyMandateSigners(
  tenantId: string,
  signal?: AbortSignal,
): Promise<{ signers: MandateSigner[]; mockIdentityEnabled: boolean }> {
  const result = await apiFetch<{ data: MandateSigner[]; mockIdentityEnabled?: boolean }>(
    companyBase(tenantId),
    { signal },
  );
  return { signers: result.data, mockIdentityEnabled: result.mockIdentityEnabled === true };
}

/** GET /transit-offices — organismos que la compañía puede elegir (AC2). */
export async function fetchCompanyTransitOffices(
  tenantId: string,
  signal?: AbortSignal,
): Promise<CompanyTransitOfficeOption[]> {
  const result = await apiFetch<{ data: CompanyTransitOfficeOption[] }>(
    `${companyBase(tenantId)}/transit-offices`,
    { signal },
  );
  return result.data;
}

/** POST — alta del mandatario en los organismos elegidos. 422 si alguno no está habilitado. */
export function createCompanyMandateSigner(
  tenantId: string,
  body: CompanyMandateSignerInput,
): Promise<MandateSignerSaved> {
  return apiFetch<MandateSignerSaved>(companyBase(tenantId), { method: "POST", body });
}

/** PUT /{signerId} — edición de datos y organismos. */
export function updateCompanyMandateSigner(
  tenantId: string,
  mandateSignerId: string,
  body: CompanyMandateSignerInput,
): Promise<MandateSignerSaved> {
  return apiFetch<MandateSignerSaved>(`${companyBase(tenantId)}/${mandateSignerId}`, {
    method: "PUT",
    body,
  });
}

/** POST /{signerId}/inactivate — baja lógica del mandatario. */
export function inactivateCompanyMandateSigner(
  tenantId: string,
  mandateSignerId: string,
): Promise<void> {
  return apiFetch<void>(`${companyBase(tenantId)}/${mandateSignerId}/inactivate`, { method: "POST" });
}

/** POST /{signerId}/reactivate — reactiva un mandatario inactivado. */
export function reactivateCompanyMandateSigner(
  tenantId: string,
  mandateSignerId: string,
): Promise<void> {
  return apiFetch<void>(`${companyBase(tenantId)}/${mandateSignerId}/reactivate`, { method: "POST" });
}
