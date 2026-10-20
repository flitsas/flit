// Cliente tipado de mandatarios (firmantes de mandato) por OT (ADR-0023, ampliado por ADR-0036).
// Módulo Admin OT (SuperAdmin u ot_admin). Endpoints acotados por transitOfficeId en la ruta.
// El número de documento y el correo son PII (Ley 1581): se reciben solo para precargar el formulario.
import { apiFetch, API_BASE_URL, getToken, friendlyErrorMessage } from "./client";
import { sessionAwareBase } from "@/lib/api/base-url";
import { companyScopedPath } from "./company-scoped-path";
import { ApiError } from "./types";

/** HU #13132 (ADR-0061) — modelo del mandatario. */
export type SignerModel = "natural" | "juridica" | "formato_blanco";
/** Forma de firma de la Persona natural. */
export type SignatureMethod = "baul" | "biometria";
/** Vigencia propia: fija o por rango de fechas. */
export type ValidityKind = "fixed" | "range";
/** Estado de vigencia calculado en el servidor. */
export type ValidityStatus = "inactivo" | "vencido" | "no_vigente" | "por_vencer" | "vigente";

/** HU #13130 — por qué un mandatario no puede firmar hoy. */
export type SignatureInvalidReason =
  | "mandatario_fuera_de_vigencia"
  | "mandatario_inactivo"
  | "sin_validacion_aprobada";

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
  /** `null` en Formato en blanco (HU #13129). */
  documentNumber: string | null;
  integrityHash: string;
  /** Correo para la validación de identidad (ADR-0036, HU #10911). PII. `null` si no se capturó. */
  email: string | null;
  /** Cuenta de usuario de OT del mandatario (ADR-0036 §D9): cotejo del firmante al aprobar. */
  userId: string | null;
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
  /** HU #13179 — compañías asociadas por organismo; vacío ⇒ aplica solo a su propia compañía. */
  officeCompanies?: MandateSignerOfficeCompanies[];
  /** HU #13129 — ausente en respuestas anteriores al cambio ⇒ Persona natural. */
  signerModel?: SignerModel;
  /** Forma de firma; `null` fuera de natural y en mandatarios anteriores al cambio. */
  signatureMethod?: SignatureMethod | null;
  validityKind?: ValidityKind;
  /** `yyyy-MM-dd`; solo con vigencia por rango. */
  validFrom?: string | null;
  validTo?: string | null;
  validityStatus?: ValidityStatus;
  /** HU #13130 — si el mandatario puede firmar hoy (vigencia y biometría vigentes). */
  signatureValid?: boolean;
  signatureInvalidReason?: SignatureInvalidReason | null;
  /**
   * HU #13134 — quién lo configuró: `organismo` (organismo de tránsito o Super Admin) o `compania`.
   * Ausente en respuestas anteriores al cambio ⇒ se trata como `compania` (sin candado).
   */
  origin?: MandateSignerOrigin;
  /** HU #13134 — el actor puede editar, inactivar y reactivar (el servidor lo calcula por rol y origen). */
  puedeEditar?: boolean;
  /** HU #13134 — el actor puede eliminar. */
  puedeEliminar?: boolean;
}

/** HU #13134 — origen de la configuración del mandatario. */
export type MandateSignerOrigin = "organismo" | "compania";

/** Campos de modelo, forma de firma y vigencia que viajan en el alta y la edición (HU #13132). */
export interface MandateSignerProfileFields {
  /** Ausente ⇒ el servidor asume `natural`. */
  signerModel?: SignerModel;
  /** Obligatoria en `natural`; con otros modelos no debe enviarse (422). */
  signatureMethod?: SignatureMethod;
  validityKind?: ValidityKind;
  /** `yyyy-MM-dd`; obligatoria con `range`. */
  validFrom?: string;
  validTo?: string;
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

export interface MandateSignerInput extends MandateSignerProfileFields {
  fullName: string;
  documentType: string;
  /** `null` en Formato en blanco: el servidor deja el documento nulo. */
  documentNumber: string | null;
  /** Correo para la validación de identidad; `null` si no se captura. */
  email: string | null;
  /** Cuenta de usuario de OT a vincular (§D9); `null` si no se asigna. */
  userId?: string | null;
  companyTenantIds: string[];
  /** HU #13124 — organismos donde aplica; ausente ⇒ solo el de la ruta. */
  transitOfficeIds?: string[];
  /** HU #13123 — id de la firma del baúl; el OT no lista el baúl, solo enviaría el id. */
  signatureVaultId?: string | null;
  /** HU #13179 — compañías asociadas por organismo (el OT ya puede enviarlas). */
  officeCompanies?: MandateSignerOfficeCompanies[];
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
  identity?: "sent" | "queued" | "reused" | "failed" | "notattempted";
}

/** HU #13135 — dónde es el único activo, qué defaults perdería y cuántos trámites sin aprobar lo usan. */
export interface MandateSignerImpact {
  hasImpact: boolean;
  /** Pares compañía × organismo donde es el único mandatario activo. */
  onlyActiveFor: { transitOfficeId: string; companyTenantId: string }[];
  /** `company_rule` (default de la compañía en un organismo) u `office` (general del organismo). */
  defaults: { kind: string; transitOfficeId: string; companyTenantId: string | null }[];
  /** Trámites radicados sin aprobar que lo usan. */
  pendingProcedures: number;
}

/** HU #13135 — conteos de la reasignación de trámites tras la baja (cabeceras del 204). */
export interface MandateSignerLifecycleOutcome {
  /** Trámites reasignados con la prelación; 0 si el servidor no los informó. */
  reassigned: number;
  /** Trámites que quedan para que el OT decida al aprobar. */
  pendingOtDecision: number;
}

/** HU #13136 — resultado de reactivar. */
export interface MandateSignerReactivation {
  restoredLinks: { transitOfficeId: string; companyTenantId: string }[];
  /** Vínculos que se restauraron inactivos porque ya hay otro mandatario activo. */
  conflictLinks: { transitOfficeId: string; companyTenantId: string }[];
  restoredDefaults: number;
}

/** Códigos de error que el servidor manda en `code` (403 y 409 de las acciones de ciclo de vida). */
export const MANDATE_SIGNER_ERROR = {
  confirmationRequired: "mandatario_baja_requiere_confirmacion",
  activeExists: "mandatario_activo_existente",
  lockedByOffice: "mandatario_configurado_por_organismo",
  noPermission: "mandatario_sin_permiso",
} as const;

function normalizeImpact(raw: Partial<MandateSignerImpact> | null | undefined): MandateSignerImpact {
  return {
    hasImpact: raw?.hasImpact ?? false,
    onlyActiveFor: raw?.onlyActiveFor ?? [],
    defaults: raw?.defaults ?? [],
    pendingProcedures: raw?.pendingProcedures ?? 0,
  };
}

/** El impacto que adjunta el 409 de confirmación requerida, o `null` si el error es otro. */
export function impactFromConfirmationError(error: unknown): MandateSignerImpact | null {
  if (!(error instanceof ApiError) || error.status !== 409) return null;
  const body = error.body as { code?: string; impact?: Partial<MandateSignerImpact> } | null | undefined;
  return body?.code === MANDATE_SIGNER_ERROR.confirmationRequired ? normalizeImpact(body.impact) : null;
}

/** Código de error del servidor (`code`) de un ApiError, si lo trae. */
export function mandateSignerErrorCode(error: unknown): string | null {
  if (!(error instanceof ApiError)) return null;
  const code = (error.body as { code?: unknown } | null | undefined)?.code;
  return typeof code === "string" ? code : null;
}

/** Lee los conteos de reasignación de las cabeceras de la respuesta. */
function captureOutcome(): {
  outcome: () => MandateSignerLifecycleOutcome;
  onResponse: (r: Response) => void;
} {
  let current: MandateSignerLifecycleOutcome = { reassigned: 0, pendingOtDecision: 0 };
  const num = (v: string | null | undefined) => {
    const n = Number.parseInt(v ?? "", 10);
    return Number.isFinite(n) && n > 0 ? n : 0;
  };
  return {
    outcome: () => current,
    onResponse: (r) => {
      current = {
        reassigned: num(r.headers?.get("X-Mandatario-Reasignados")),
        pendingOtDecision: num(r.headers?.get("X-Mandatario-Pendientes-Decision-OT")),
      };
    },
  };
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

/** HU #13178 — compañía asociable a un mandatario: solo id de tenant, nombre y NIT (Ley 1581). */
export interface AssociableCompany {
  id: string;
  name: string;
  nit: string;
}

export interface AssociableCompaniesQuery {
  /** Nombre o NIT; el servidor exige mínimo 2 caracteres (422 si trae menos). */
  search?: string;
  page?: number;
  pageSize?: number;
  /** HU #13248b — pide la lista completa en una sola respuesta (el servidor la topa en 1000); ignora page y pageSize. */
  all?: boolean;
}

export interface AssociableCompaniesPage {
  items: AssociableCompany[];
  total: number;
  page: number;
  pageSize: number;
  /** Verdadero si quien consulta no tiene red: no hay lista y el mandatario aplica solo a su compañía. */
  aplicaSoloASuCompania: boolean;
}

function normalizarAsociables(r: Partial<AssociableCompaniesPage> | null | undefined): AssociableCompaniesPage {
  const items = Array.isArray(r?.items) ? r!.items : [];
  return {
    items,
    total: r?.total ?? items.length,
    page: r?.page ?? 1,
    pageSize: r?.pageSize ?? items.length,
    aplicaSoloASuCompania: r?.aplicaSoloASuCompania === true,
  };
}

/**
 * GET /associable-companies — HU #13178/#13182: todas las compañías gestoras activas (sin duplicados
 * por NIT), con búsqueda por nombre y NIT y paginación de servidor. Solo OT admin y SuperAdmin (403
 * al resto). No altera la visibilidad de la bandeja de trámites.
 */
export async function fetchOtAssociableCompanies(
  transitOfficeId: string,
  query: AssociableCompaniesQuery = {},
  signal?: AbortSignal,
): Promise<AssociableCompaniesPage> {
  const r = await apiFetch<AssociableCompaniesPage>(`${base(transitOfficeId)}/associable-companies`, {
    query: { search: query.search, page: query.page, pageSize: query.pageSize, all: query.all },
    signal,
  });
  return normalizarAsociables(r);
}

/** GET /companies — compañías del OT con sus mandatarios asignados (RF34 + multiselect). */
export async function fetchOtCompanies(
  transitOfficeId: string,
  signal?: AbortSignal,
): Promise<OtCompany[]> {
  const result = await apiFetch<{ data: OtCompany[] }>(`${base(transitOfficeId)}/companies`, { signal });
  return result.data;
}

/**
 * POST — alta de mandatario (RF22) desde el hub del OT. Solo ot_admin o SuperAdmin (403 para el
 * resto, HU #13123). Lanza ApiValidationError en 422 (RF33).
 */
export function createMandateSigner(
  transitOfficeId: string,
  body: MandateSignerInput,
): Promise<MandateSignerSaved> {
  return apiFetch<MandateSignerSaved>(base(transitOfficeId), { method: "POST", body });
}

/** HU #13248 — respuesta del reenvío: `sent` (enlace enviado) o `queued` (se reintenta solo). */
export interface MandateSignerIdentityResend {
  identity: "sent" | "queued";
  validationId?: string;
}

/**
 * POST /{signerId}/identity-validation/resend — «Reenviar validación» desde el hub del OT. Solo
 * ot_admin y SuperAdmin. 409 si el mandatario no requiere validación, 422 `email` si no tiene correo,
 * 502 si el proveedor rechaza el envío.
 */
export function resendMandateSignerIdentity(
  transitOfficeId: string,
  mandateSignerId: string,
): Promise<MandateSignerIdentityResend> {
  return apiFetch<MandateSignerIdentityResend>(
    `${base(transitOfficeId)}/${mandateSignerId}/identity-validation/resend`,
    { method: "POST" },
  );
}

/** Respuesta de «Consultar estado»: estado vigente de la validación propia y si la consulta lo cambió. */
export interface MandateSignerIdentityReconcile {
  status: string | null;
  updated: boolean;
}

/**
 * POST /{signerId}/identity-validation/reconcile — «Consultar estado» desde el hub del OT: pregunta al
 * proveedor y aplica el resultado si el webhook no llegó (lo mismo que hace la pantalla de espera del
 * trámite). 409 si no requiere validación o aún no tiene ninguna; 503 si el proveedor no responde.
 */
export function reconcileMandateSignerIdentity(
  transitOfficeId: string,
  mandateSignerId: string,
): Promise<MandateSignerIdentityReconcile> {
  return apiFetch<MandateSignerIdentityReconcile>(
    `${base(transitOfficeId)}/${mandateSignerId}/identity-validation/reconcile`,
    { method: "POST" },
  );
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

/**
 * POST /{signerId}/inactivate — baja lógica que libera compañías (RF24). Devuelve cuántos trámites
 * se reasignaron y cuántos quedan para decisión del OT (cabeceras del 204, HU #13135).
 */
export async function inactivateMandateSigner(
  transitOfficeId: string,
  mandateSignerId: string,
): Promise<MandateSignerLifecycleOutcome> {
  const { outcome, onResponse } = captureOutcome();
  await apiFetch<void>(`${base(transitOfficeId)}/${mandateSignerId}/inactivate`, {
    method: "POST",
    onResponse,
  });
  return outcome();
}

/**
 * POST /{signerId}/reactivate — reactiva un mandatario inactivado. El 200 informa los vínculos que
 * se restauraron y los que no (porque ya hay otro activo), sin desplazar al vigente (HU #13136).
 */
export function reactivateMandateSigner(
  transitOfficeId: string,
  mandateSignerId: string,
): Promise<MandateSignerReactivation> {
  return apiFetch<MandateSignerReactivation>(`${base(transitOfficeId)}/${mandateSignerId}/reactivate`, {
    method: "POST",
  });
}

/** GET /{signerId}/impact — qué perdería la baja del mandatario (solo lectura, HU #13135). */
export async function fetchMandateSignerImpact(
  transitOfficeId: string,
  mandateSignerId: string,
  signal?: AbortSignal,
): Promise<MandateSignerImpact> {
  const r = await apiFetch<{ data: MandateSignerImpact }>(
    `${base(transitOfficeId)}/${mandateSignerId}/impact`,
    { signal },
  );
  return normalizeImpact(r.data);
}

/**
 * DELETE /{signerId} — eliminación (baja lógica, se conserva el historial). Con impacto exige
 * `confirmarImpacto`: sin ella responde 409 `mandatario_baja_requiere_confirmacion`.
 */
export async function deleteMandateSigner(
  transitOfficeId: string,
  mandateSignerId: string,
  confirmarImpacto: boolean,
): Promise<MandateSignerLifecycleOutcome> {
  const { outcome, onResponse } = captureOutcome();
  await apiFetch<void>(`${base(transitOfficeId)}/${mandateSignerId}`, {
    method: "DELETE",
    query: { confirmarImpacto },
    onResponse,
  });
  return outcome();
}

/** GET PNG de la firma del baúl del mandatario (preview del ojo). 404 si no hay imagen. */
export async function fetchMandateSignerSignatureImage(
  transitOfficeId: string,
  mandateSignerId: string,
  signal?: AbortSignal,
): Promise<Blob> {
  const baseUrl =
    sessionAwareBase(API_BASE_URL) || (typeof window !== "undefined" ? window.location.origin : "http://localhost:3000");
  const url = new URL(`${base(transitOfficeId)}/${mandateSignerId}/signature-image`, baseUrl);
  const token = getToken();
  const headers: Record<string, string> = {};
  if (token) headers.Authorization = `Bearer ${token}`;

  const response = await fetch(url.toString(), { method: "GET", headers, signal });
  if (!response.ok) {
    let detail: unknown = null;
    try {
      const text = await response.text();
      detail = text ? JSON.parse(text) : null;
    } catch {
      /* ignore */
    }
    throw new ApiError(
      response.status,
      friendlyErrorMessage(detail as Record<string, unknown> | null),
      detail,
    );
  }

  const blob = await response.blob();
  return blob.type.startsWith("image/") ? blob : new Blob([blob], { type: "image/png" });
}

// HU #11759 (ADR-0050, DA-5) — se retiran `sendMandateSignerIdentity`, `resendMandateSignerIdentity`,
// `linkMandateSignerIdentity` y `mockMandateSignerIdentity` (OT-scoped, HU #10911/#11028): huérfanas
// desde la HU #11202 (confirmado por grep, cero consumidores) y, además, las rutas que llamaban ya
// responden 410 Gone desde la HU #11758.

// ── HU #11202 — mandatarios desde el configurador de la COMPAÑÍA ──────────────
// Vista inversa: la empresa registra a la persona y marca en cuáles de SUS organismos aplica, en vez
// de que cada organismo elija compañías. Mismos objetos de dominio; cambia la ruta y quién manda.

/** Organismo de tránsito habilitado para la compañía (opción del multiselect). */
export interface CompanyTransitOfficeOption {
  transitOfficeId: string;
  code: string;
  name: string;
  /** Nombre vigente del formato de contrato de ese organismo (HU #13174). */
  formatName?: string;
}

/** Datos que la compañía captura de un mandatario. */
export interface CompanyMandateSignerInput extends MandateSignerProfileFields {
  fullName: string;
  documentType: string;
  /** `null` en Formato en blanco. */
  documentNumber: string | null;
  email: string | null;
  /** Organismos donde aplica. Al editar, REEMPLAZA a los anteriores: quitar uno lo retira. */
  transitOfficeIds: string[];
  /**
   * Firma del baúl elegida para el mandatario. `null` ⇒ el trámite la resuelve por documento, que es
   * el comportamiento previo.
   */
  signatureVaultId?: string | null;
  /**
   * Compañías asociadas por organismo. En la edición cada organismo presente REEMPLAZA su conjunto
   * (lista vacía = retirar); ausente no toca nada.
   */
  officeCompanies?: MandateSignerOfficeCompanies[];
}

/**
 * HU #13179 — compañías de FLIT (por id de tenant) a las que se asocia el mandatario en un organismo.
 * Lista vacía o ausente ⇒ aplica solo a su propia compañía. Reemplaza a las «empresas representadas».
 */
export interface MandateSignerOfficeCompanies {
  transitOfficeId: string;
  associatedCompanyTenantIds: string[];
  /** Nombre y NIT de las asociadas (solo esos datos); ausente en respuestas antiguas. */
  associatedCompanies?: MandateSignerAssociatedCompany[];
}

export interface MandateSignerAssociatedCompany {
  id: string;
  name: string;
  nit: string;
}

/**
 * GET /associable-companies — HU #13178/#13181: lo que el Admin de Compañía puede asociar (solo sus
 * hijas directas activas). Sin red: `items` vacío y `aplicaSoloASuCompania` verdadero.
 */
export async function fetchCompanyAssociableCompanies(
  tenantId: string,
  query: AssociableCompaniesQuery = {},
  signal?: AbortSignal,
  networkHeadId?: string | null,
): Promise<AssociableCompaniesPage> {
  const r = await apiFetch<AssociableCompaniesPage>(
    `${companyBase(tenantId, networkHeadId)}/associable-companies`,
    { query: { search: query.search, page: query.page, pageSize: query.pageSize, all: query.all }, signal },
  );
  return normalizarAsociables(r);
}

// HU #11757 (ADR-0050) — se retira `mandateSignerIdentityAction` (send/resend/link desde el
// configurador de la COMPAÑÍA): confirmado por grep, sin otro consumidor real (solo un mock de test).
// El módulo Identidad es la única fuente que puede originar una validación; esa ruta también
// responderá 410 Gone (HU #11758).

function companyBase(tenantId: string, networkHeadId?: string | null): string {
  return companyScopedPath(tenantId, "/mandate-signers", networkHeadId);
}

/** GET — mandatarios de la compañía, con sus organismos. */
export async function fetchCompanyMandateSigners(
  tenantId: string,
  signal?: AbortSignal,
  networkHeadId?: string | null,
): Promise<MandateSigner[]> {
  const result = await apiFetch<{ data: MandateSigner[] }>(companyBase(tenantId, networkHeadId), {
    signal,
  });
  return result.data;
}

/** GET /transit-offices — organismos que la compañía puede elegir (AC2). */
export async function fetchCompanyTransitOffices(
  tenantId: string,
  signal?: AbortSignal,
  networkHeadId?: string | null,
): Promise<CompanyTransitOfficeOption[]> {
  const result = await apiFetch<{ data: CompanyTransitOfficeOption[] }>(
    `${companyBase(tenantId, networkHeadId)}/transit-offices`,
    { signal },
  );
  return result.data;
}

/** POST — alta del mandatario en los organismos elegidos. 422 si alguno no está habilitado. */
export function createCompanyMandateSigner(
  tenantId: string,
  body: CompanyMandateSignerInput,
  networkHeadId?: string | null,
): Promise<MandateSignerSaved> {
  return apiFetch<MandateSignerSaved>(companyBase(tenantId, networkHeadId), { method: "POST", body });
}

/** PUT /{signerId} — edición de datos y organismos. */
export function updateCompanyMandateSigner(
  tenantId: string,
  mandateSignerId: string,
  body: CompanyMandateSignerInput,
  networkHeadId?: string | null,
): Promise<MandateSignerSaved> {
  return apiFetch<MandateSignerSaved>(`${companyBase(tenantId, networkHeadId)}/${mandateSignerId}`, {
    method: "PUT",
    body,
  });
}

/** HU #13248 — «Reenviar validación» desde la compañía (mismas respuestas que el hub del OT). */
export function resendCompanyMandateSignerIdentity(
  tenantId: string,
  mandateSignerId: string,
  networkHeadId?: string | null,
): Promise<MandateSignerIdentityResend> {
  return apiFetch<MandateSignerIdentityResend>(
    `${companyBase(tenantId, networkHeadId)}/${mandateSignerId}/identity-validation/resend`,
    { method: "POST" },
  );
}

/** «Consultar estado» desde la compañía (mismas respuestas que el hub del OT). */
export function reconcileCompanyMandateSignerIdentity(
  tenantId: string,
  mandateSignerId: string,
  networkHeadId?: string | null,
): Promise<MandateSignerIdentityReconcile> {
  return apiFetch<MandateSignerIdentityReconcile>(
    `${companyBase(tenantId, networkHeadId)}/${mandateSignerId}/identity-validation/reconcile`,
    { method: "POST" },
  );
}

/** POST /{signerId}/inactivate — baja lógica del mandatario; informa los trámites reasignados. */
export async function inactivateCompanyMandateSigner(
  tenantId: string,
  mandateSignerId: string,
  networkHeadId?: string | null,
): Promise<MandateSignerLifecycleOutcome> {
  const { outcome, onResponse } = captureOutcome();
  await apiFetch<void>(`${companyBase(tenantId, networkHeadId)}/${mandateSignerId}/inactivate`, {
    method: "POST",
    onResponse,
  });
  return outcome();
}

/** POST /{signerId}/reactivate — reactiva un mandatario inactivado sin desplazar al vigente. */
export function reactivateCompanyMandateSigner(
  tenantId: string,
  mandateSignerId: string,
  networkHeadId?: string | null,
): Promise<MandateSignerReactivation> {
  return apiFetch<MandateSignerReactivation>(
    `${companyBase(tenantId, networkHeadId)}/${mandateSignerId}/reactivate`,
    { method: "POST" },
  );
}

/** GET /{signerId}/impact — qué perdería la baja del mandatario (solo lectura). */
export async function fetchCompanyMandateSignerImpact(
  tenantId: string,
  mandateSignerId: string,
  signal?: AbortSignal,
  networkHeadId?: string | null,
): Promise<MandateSignerImpact> {
  const r = await apiFetch<{ data: MandateSignerImpact }>(
    `${companyBase(tenantId, networkHeadId)}/${mandateSignerId}/impact`,
    { signal },
  );
  return normalizeImpact(r.data);
}

/** DELETE /{signerId} — eliminación (baja lógica, conserva el historial); con impacto exige confirmarlo. */
export async function deleteCompanyMandateSigner(
  tenantId: string,
  mandateSignerId: string,
  confirmarImpacto: boolean,
  networkHeadId?: string | null,
): Promise<MandateSignerLifecycleOutcome> {
  const { outcome, onResponse } = captureOutcome();
  await apiFetch<void>(`${companyBase(tenantId, networkHeadId)}/${mandateSignerId}`, {
    method: "DELETE",
    query: { confirmarImpacto },
    onResponse,
  });
  return outcome();
}
