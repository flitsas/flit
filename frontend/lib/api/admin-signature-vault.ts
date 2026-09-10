// Cliente tipado del Baúl de Firmas de apoderados por compañía (HU #10644, backend #10643).
// Endpoints SuperAdmin acotados por tenantId. El binario de la firma NUNCA se devuelve:
// la lista y el detalle solo exponen metadatos + referencias de almacenamiento (storagePath).
import { apiFetch } from "./client";

/** Estado de una firma en el baúl. `activa` reutilizable; `revocada` inhabilitada. */
export type SignatureVaultEstado = "activa" | "revocada" | "vencida";

/**
 * Firma de apoderado registrada en el baúl (solo metadatos). El artefacto de firma
 * (PNG) no viaja al cliente: solo se conservan las referencias de almacenamiento.
 */
export interface SignatureVaultItem {
  id: string;
  documentType: string;
  documentNumber: string;
  /** NIT de la empresa: deprecado desde la HU #10930 (la firma es de la persona). */
  nitEmpresa?: string | null;
  fullName: string;
  /** Código hash alfanumérico digitado por el usuario (opcional). */
  codigoHash?: string | null;
  /** Vigencia (YYYY-MM-DD). */
  vigenciaDesde: string;
  vigenciaHasta: string;
  estado: SignatureVaultEstado;
  /** Referencia de almacenamiento del artefacto (nunca el binario). */
  storagePath?: string | null;
  /** Fecha de registro; el nombre del campo puede variar según backend. */
  fechaRegistro?: string | null;
  createdAt?: string | null;
  mandateSignerId?: string | null;
}

/**
 * Payload de edición (PUT). Solo los campos corregibles: el documento identifica a la persona y el
 * artefacto ya se estampó en lo emitido, así que ninguno de los dos se edita en sitio.
 */
export interface SignatureVaultEditInput {
  fullName: string;
  codigoHash?: string | null;
  vigenciaDesde: string;
  vigenciaHasta: string;
}

/** Payload de alta de firma (POST). `artefactoFirmaBase64` acepta el prefijo data:image/png. */
export interface SignatureVaultInput {
  documentType: string;
  documentNumber: string;
  /** NIT de la empresa: opcional/deprecado desde la HU #10930 (la firma es de la persona). */
  nitEmpresa?: string | null;
  fullName: string;
  /** Código hash alfanumérico digitado por el usuario (opcional). */
  codigoHash?: string | null;
  vigenciaDesde: string;
  vigenciaHasta: string;
  artefactoFirmaBase64: string;
  mandateSignerId?: string | null;
}

/** Respuesta del alta (201). */
export interface SignatureVaultCreated {
  id: string;
}

function base(tenantId: string): string {
  return `/api/v1/admin/companies/${tenantId}/signature-vault`;
}

// El backend puede responder un arreglo plano o envuelto en `{ data: [] }`; se normaliza.
function unwrapList(result: unknown): SignatureVaultItem[] {
  if (Array.isArray(result)) {
    return result as SignatureVaultItem[];
  }
  const data = (result as { data?: SignatureVaultItem[] } | null)?.data;
  return Array.isArray(data) ? data : [];
}

/** GET "" — firmas del baúl de la compañía (solo metadatos, sin binario). */
export async function fetchSignatureVault(
  tenantId: string,
  signal?: AbortSignal,
): Promise<SignatureVaultItem[]> {
  const result = await apiFetch<unknown>(base(tenantId), { signal });
  return unwrapList(result);
}

/**
 * GET "" con filtros de documento — firmas vigentes de UNA persona (HU #11180 AC1).
 * El backend acepta `documentType`, `documentNumber` y `soloVigentes` como query params.
 * Solo devuelve las firmas de la persona indicada, filtradas por vigencia si `soloVigentes=true`.
 */
export async function fetchSignatureVaultByDocument(
  tenantId: string,
  documentType: string,
  documentNumber: string,
  soloVigentes = true,
  signal?: AbortSignal,
): Promise<SignatureVaultItem[]> {
  const result = await apiFetch<unknown>(base(tenantId), {
    query: { documentType, documentNumber, soloVigentes },
    signal,
  });
  return unwrapList(result);
}

/** GET "/{id}" — detalle de una firma (solo metadatos). */
export function fetchSignatureVaultItem(
  tenantId: string,
  id: string,
  signal?: AbortSignal,
): Promise<SignatureVaultItem> {
  return apiFetch<SignatureVaultItem>(`${base(tenantId)}/${id}`, { signal });
}

/**
 * POST "" — registra una firma. Lanza ApiValidationError en 422 con `errors[]`
 * (codes: requerido, vigencia_invalida, artefacto_invalido, firma_activa_existente).
 */
export function createSignatureVaultEntry(
  tenantId: string,
  body: SignatureVaultInput,
): Promise<SignatureVaultCreated> {
  return apiFetch<SignatureVaultCreated>(base(tenantId), {
    method: "POST",
    body: { mandateSignerId: null, ...body },
  });
}

/**
 * PUT "/{id}" — corrige los datos capturados de una firma ACTIVA (204).
 *
 * No admite cambiar el documento (identifica a la persona dueña de la firma) ni el artefacto (lo ya
 * emitido se estampó con esa imagen): para eso se captura una firma nueva, que revoca la anterior y la
 * conserva. Lanza ApiValidationError en 422 (requerido, vigencia_invalida, codigo_hash_invalido).
 */
export function updateSignatureVaultEntry(
  tenantId: string,
  id: string,
  body: SignatureVaultEditInput,
): Promise<void> {
  return apiFetch<void>(`${base(tenantId)}/${id}`, { method: "PUT", body });
}

/** POST "/{id}/revoke" — anula una firma (204, idempotente). */
export function revokeSignatureVaultEntry(tenantId: string, id: string): Promise<void> {
  return apiFetch<void>(`${base(tenantId)}/${id}/revoke`, { method: "POST" });
}
