// Cliente tipado de documentos personalizados por compañía (HU #11313/#11314/#11315, Feature #11309,
// ADR-0042). Contrato REAL leído de `AdminPersonalizedDocumentsEndpoints.cs` (no del plan técnico a
// ciegas): seis rutas bajo `/api/v1/admin/companies/{tenantId}/personalized-documents`. El PDF NUNCA
// viaja en el request del API: se sube DIRECTO a storage con una presigned POST policy (los campos
// firmados van ANTES del `file` en el multipart, igual que en escrituras — `admin-deeds.ts`) y la
// única oportunidad de validar los bytes es `confirm` (recalcula SHA-256, abre el PDF, cuenta
// páginas). Una versión rechazada en `confirm` (422) NUNCA llega a `activo`/`historico`, así que
// `fetchPersonalizedDocuments` la filtra del historial junto con las `pendiente` abandonadas (basura
// recuperable, sin versión real que mostrar).
import { apiFetch } from "./client";
import { ApiError, ApiValidationError } from "./types";

/** Vocabulario cerrado de tipos de documento personalizable (espejo de `PersonalizedDocumentTypes`). */
export type PersonalizedDocumentType = "mandato" | "tramite_virtual";

export const PERSONALIZED_DOCUMENT_TYPES: PersonalizedDocumentType[] = ["mandato", "tramite_virtual"];

/** Estados de una versión (espejo de `CompanyPersonalizedDocumentStatus`). */
export type PersonalizedDocumentStatus = "pendiente" | "activo" | "historico" | "rechazado";

/** Una versión del historial, tal como la devuelve `GET ""` (contrato §8 del plan, `ListAsync`). */
export interface PersonalizedDocumentVersion {
  id: string;
  version: number;
  status: PersonalizedDocumentStatus;
  isActive: boolean;
  filename: string;
  sha256: string;
  pageCount: number | null;
  createdAt: string;
  createdBy: string | null;
  activatedAt: string | null;
  activatedBy: string | null;
  deactivatedAt: string | null;
  deactivatedBy: string | null;
}

/** Agrupación por tipo de documento: versión vigente (si hay) + historial. */
export interface PersonalizedDocumentGroup {
  documentType: string;
  active: PersonalizedDocumentVersion | null;
  history: PersonalizedDocumentVersion[];
}

/** Presigned POST policy para subir el PDF DIRECTO a storage (campos ANTES del `file`). */
export interface PersonalizedDocumentUploadTicket {
  storagePath: string;
  url: string;
  fields: Record<string, string>;
}

/** Respuesta de `POST ""`: alta en `pendiente` + ticket de subida. */
export interface CreatePersonalizedDocumentVersionResponse {
  id: string;
  version: number;
  upload: PersonalizedDocumentUploadTicket;
}

/** Respuesta de `POST "/{id}/confirm"` cuando la versión queda `activo`. */
export interface ConfirmPersonalizedDocumentVersionResponse {
  id: string;
  version: number;
  status: "activo";
  sha256: string;
  pageCount: number;
}

/** Respuesta de `POST "/{id}/activate"`. */
export interface ActivatePersonalizedDocumentVersionResponse {
  id: string;
  version: number;
  status: "activo";
}

/** Respuesta de `GET "/{id}/view"`: presigned GET inline, vida corta. */
export interface PersonalizedDocumentViewResponse {
  url: string;
  expiresAt: string;
}

/** Error de campo del sobre `{ errors: [{ field, code, message }] }` (422). */
export interface PersonalizedDocumentValidationError {
  field: string;
  code: string;
  message: string;
}

function base(tenantId: string): string {
  return `/api/v1/admin/companies/${tenantId}/personalized-documents`;
}

/** SHA-256 hex (minúsculas) del PDF, calculado en el navegador (integridad declarada del artefacto). */
export async function sha256Hex(file: File): Promise<string> {
  const buffer = await file.arrayBuffer();
  const digest = await crypto.subtle.digest("SHA-256", buffer);
  return Array.from(new Uint8Array(digest))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
}

/**
 * `GET ""` — historial agrupado por tipo. El GET no exige el canal `TENANT_API` (el histórico se
 * sigue consultando tras volver a `FLIT_SMTP`). Filtra del `history` las filas `pendiente`
 * (subida sin confirmar: basura recuperable) y `rechazado` (el archivo rechazado NO forma parte
 * del historial visible — decisión de UI, el backend las conserva para diagnóstico).
 */
export async function fetchPersonalizedDocuments(
  tenantId: string,
  signal?: AbortSignal,
): Promise<PersonalizedDocumentGroup[]> {
  const response = await apiFetch<{ documents: PersonalizedDocumentGroup[] }>(base(tenantId), { signal });
  return response.documents.map((group) => ({
    ...group,
    history: group.history.filter((v) => v.status === "activo" || v.status === "historico"),
  }));
}

/** `POST ""` — registra una versión nueva en `pendiente` y devuelve el ticket de subida. */
export function createPersonalizedDocumentVersion(
  tenantId: string,
  input: { documentType: PersonalizedDocumentType; filename: string; sha256: string; sizeBytes: number },
): Promise<CreatePersonalizedDocumentVersionResponse> {
  return apiFetch<CreatePersonalizedDocumentVersionResponse>(base(tenantId), {
    method: "POST",
    body: {
      documentType: input.documentType,
      filename: input.filename,
      sha256: input.sha256,
      sizeBytes: input.sizeBytes,
    },
  });
}

/**
 * Sube el PDF DIRECTO al storage con la presigned POST policy. Los campos firmados van SIEMPRE
 * ANTES del `file` en el `FormData`: el almacenamiento rechaza la petición si el orden se invierte.
 */
export async function uploadPersonalizedDocumentFile(
  upload: PersonalizedDocumentUploadTicket,
  file: File,
): Promise<void> {
  const form = new FormData();
  for (const [key, value] of Object.entries(upload.fields)) {
    form.append(key, value);
  }
  form.append("file", file);
  const res = await fetch(upload.url, { method: "POST", body: form });
  if (!res.ok) {
    const detail = await res.text().catch(() => "");
    throw new Error(`Error subiendo el PDF a almacenamiento (${res.status})${detail ? ": " + detail : ""}`);
  }
}

/**
 * `POST "/{id}/confirm"` — única oportunidad de ver los bytes: si el PDF no pasa la validación de
 * servidor, lanza `ApiValidationError` (422, `errors[0].code` es el motivo estable: `sha256_mismatch`
 * | `no_es_pdf` | `pdf_cifrado` | `pdf_ilegible` | `excede_paginas` | `excede_tamano`) y la versión
 * queda `rechazado` (no aparece en `fetchPersonalizedDocuments`).
 */
export function confirmPersonalizedDocumentVersion(
  tenantId: string,
  id: string,
): Promise<ConfirmPersonalizedDocumentVersionResponse> {
  return apiFetch<ConfirmPersonalizedDocumentVersionResponse>(`${base(tenantId)}/${id}/confirm`, {
    method: "POST",
  });
}

/**
 * `POST "/{id}/activate"` — reactiva una versión `historico`. 409 `version_no_activable` si está en
 * `pendiente`/`rechazado` (no debería llegar aquí desde la UI, que solo ofrece reactivar `historico`).
 */
export function activatePersonalizedDocumentVersion(
  tenantId: string,
  id: string,
): Promise<ActivatePersonalizedDocumentVersionResponse> {
  return apiFetch<ActivatePersonalizedDocumentVersionResponse>(`${base(tenantId)}/${id}/activate`, {
    method: "POST",
  });
}

/** `DELETE "/{documentType}"` — vuelve al documento del sistema; conserva el historial, idempotente. */
export function deactivatePersonalizedDocument(
  tenantId: string,
  documentType: PersonalizedDocumentType,
): Promise<void> {
  return apiFetch<void>(`${base(tenantId)}/${documentType}`, { method: "DELETE" });
}

/** `GET "/{id}/view"` — presigned GET inline para previsualizar SIN activar. */
export function getPersonalizedDocumentView(
  tenantId: string,
  id: string,
): Promise<PersonalizedDocumentViewResponse> {
  return apiFetch<PersonalizedDocumentViewResponse>(`${base(tenantId)}/${id}/view`, {});
}

/** `true` si el error HTTP es el 409 `canal_no_habilitado` (DT-7: rutas de escritura sin TENANT_API). */
export function isChannelNotEnabled(error: unknown): boolean {
  return (
    error instanceof ApiError &&
    error.status === 409 &&
    typeof error.body === "object" &&
    error.body !== null &&
    (error.body as { error?: string }).error === "canal_no_habilitado"
  );
}

/** `true` si el error HTTP es el 409 `version_no_activable` (versión en `pendiente`/`rechazado`). */
export function isVersionNotActivable(error: unknown): boolean {
  return (
    error instanceof ApiError &&
    error.status === 409 &&
    typeof error.body === "object" &&
    error.body !== null &&
    (error.body as { error?: string }).error === "version_no_activable"
  );
}

/** Primer error de campo de un `ApiValidationError` de este módulo, tipado con `code`. */
export function firstValidationError(
  error: ApiValidationError,
): PersonalizedDocumentValidationError | null {
  const raw = error.errors[0] as unknown as PersonalizedDocumentValidationError | undefined;
  return raw ?? null;
}

/**
 * Orquesta el alta de una versión: (1) SHA-256 en el navegador, (2) `POST ""` (ticket de subida),
 * (3) subida directa al storage, (4) `POST confirm` (única validación real de los bytes). Propaga
 * `ApiValidationError` (422, motivo accionable) y `ApiError` 409 `canal_no_habilitado`.
 */
export async function uploadAndConfirmPersonalizedDocument(
  tenantId: string,
  documentType: PersonalizedDocumentType,
  file: File,
): Promise<ConfirmPersonalizedDocumentVersionResponse> {
  const sha256 = await sha256Hex(file);
  const created = await createPersonalizedDocumentVersion(tenantId, {
    documentType,
    filename: file.name,
    sha256,
    sizeBytes: file.size,
  });
  await uploadPersonalizedDocumentFile(created.upload, file);
  return confirmPersonalizedDocumentVersion(tenantId, created.id);
}
