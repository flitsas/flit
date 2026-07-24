// Cliente tipado del directorio de representantes legales por compañía (HU #10904, backend #10901,
// ADR-0033). Endpoints SuperAdmin acotados por tenantId. El número de documento y el NIT son PII
// (Ley 1581): solo viajan en respuestas autenticadas de gestión y nunca deben loguearse.
import { apiFetch } from "./client";

/**
 * Representante legal proyectado para la gestión admin. Incluye los datos denormalizados de la
 * compañía representada (NIT + nombre), las referencias de firma/identidad vigentes y los tipos de
 * trámite del puente M:N. `hasSignatureOrIdentity` resume si el representante puede firmar hoy.
 */
export interface LegalRepresentativeItem {
  id: string;
  representedCompanyId: string;
  /** NIT de la compañía representada (PII). */
  companyDocumentNumber: string;
  companyName: string;
  documentType: string;
  documentNumber: string;
  firstLastName: string;
  secondLastName?: string | null;
  /** Nombres del representante. */
  name: string;
  email?: string | null;
  address?: string | null;
  city?: string | null;
  phone?: string | null;
  signatureVaultId?: string | null;
  identityValidationRef?: string | null;
  hasSignatureOrIdentity: boolean;
  procedureTypeIds: string[];
  isActive: boolean;
  createdAt: string;
  updatedAt?: string | null;
}

/** Página de representantes: `{ data, totalCount, page, pageSize }` (igual que el audit log). */
export interface LegalRepresentativePage {
  data: LegalRepresentativeItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

/**
 * Payload de alta/edición (POST/PUT). Lleva los datos de la compañía representada (se upserta por
 * NIT) y del representante, más los tipos de trámite que puede firmar.
 */
export interface LegalRepresentativeInput {
  companyNit: string;
  companyName: string;
  companyEmail?: string | null;
  companyAddress?: string | null;
  companyCity?: string | null;
  companyPhone?: string | null;
  documentType: string;
  documentNumber: string;
  firstLastName: string;
  secondLastName?: string | null;
  name: string;
  email?: string | null;
  address?: string | null;
  city?: string | null;
  phone?: string | null;
  procedureTypeIds: string[];
}

/**
 * Tipo de trámite asignable a un representante (activo + publicado), tal como lo devuelve el
 * catálogo real (`tramites.procedure_types`). El `id` es el del catálogo del entorno: NUNCA debe
 * hardcodearse en el frontend (los seeds usan `uuidv7()`, no deterministas por BD).
 */
export interface AssignableProcedureType {
  id: string;
  code: string;
  name: string;
}

/** Señal estable que el guardado puede emitir (no es un error 422): el registro persistió igual. */
export const SIGNAL_SIN_FIRMA_NI_IDENTIDAD = "sin_firma_ni_identidad";

/** Respuesta del guardado (201/200): id + señales no bloqueantes. */
export interface LegalRepresentativeSaved {
  id: string;
  signals: string[];
}

/** Respuesta del envío de validación de identidad (POST .../identity/send, HU #10907). */
export interface IdentityValidationSent {
  id: string;
  status: string;
  captureUrl?: string | null;
  validUntil?: string | null;
  reused: boolean;
}

function base(tenantId: string): string {
  return `/api/v1/admin/companies/${tenantId}/legal-representatives`;
}

/** GET "" — página de representantes de la compañía. */
export function fetchLegalRepresentatives(
  tenantId: string,
  page: number,
  pageSize: number,
  signal?: AbortSignal,
): Promise<LegalRepresentativePage> {
  return apiFetch<LegalRepresentativePage>(base(tenantId), {
    query: { page, pageSize },
    signal,
  });
}

/** GET "/{id}" — un representante por id. */
export function fetchLegalRepresentative(
  tenantId: string,
  id: string,
  signal?: AbortSignal,
): Promise<LegalRepresentativeItem> {
  return apiFetch<LegalRepresentativeItem>(`${base(tenantId)}/${id}`, { signal });
}

/**
 * POST "" — alta. Lanza ApiValidationError en 422 con `errors[]` (codes: `requerido`,
 * `tipo_tramite_invalido`, `tipo_tramite_inexistente`). En 201 devuelve id + señales.
 */
export function createLegalRepresentative(
  tenantId: string,
  body: LegalRepresentativeInput,
): Promise<LegalRepresentativeSaved> {
  return apiFetch<LegalRepresentativeSaved>(base(tenantId), { method: "POST", body });
}

/** PUT "/{id}" — edición. 404 si no existe, 422 si inválido; en 200 devuelve id + señales. */
export function updateLegalRepresentative(
  tenantId: string,
  id: string,
  body: LegalRepresentativeInput,
): Promise<LegalRepresentativeSaved> {
  return apiFetch<LegalRepresentativeSaved>(`${base(tenantId)}/${id}`, { method: "PUT", body });
}

/** DELETE "/{id}" — baja lógica idempotente (204). */
export function deleteLegalRepresentative(tenantId: string, id: string): Promise<void> {
  return apiFetch<void>(`${base(tenantId)}/${id}`, { method: "DELETE" });
}

/**
 * GET "/procedure-types" — tipos de trámite asignables (activos + publicados) con sus IDs reales del
 * catálogo. El multiselect del formulario se alimenta de aquí en vez de una lista estática, para que
 * los ids enviados existan en `tramites.procedure_types` del entorno (corrige `tipo_tramite_inexistente`).
 */
export function fetchAssignableProcedureTypes(
  tenantId: string,
  signal?: AbortSignal,
): Promise<AssignableProcedureType[]> {
  return apiFetch<AssignableProcedureType[]>(`${base(tenantId)}/procedure-types`, { signal });
}

/**
 * POST "/{id}/identity/send" — inicia la validación de identidad por correo (HU #10907). Lanza
 * ApiValidationError en 422 (`email_requerido`) y ApiError en 502/503 (proveedor no disponible).
 */
export function sendLegalRepresentativeIdentity(
  tenantId: string,
  id: string,
): Promise<IdentityValidationSent> {
  return apiFetch<IdentityValidationSent>(`${base(tenantId)}/${id}/identity/send`, {
    method: "POST",
  });
}
