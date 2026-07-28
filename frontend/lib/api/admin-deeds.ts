// Cliente tipado de las escrituras (PDF) por compañía (HU #10905, backend #10902, ADR-0033).
// Endpoints SuperAdmin acotados por tenantId. El PDF NUNCA viaja en el request del API ni se guarda en
// BD: se sube DIRECTO a S3 con una presigned POST policy y se ve con una presigned GET de vida corta.
// El cliente calcula el SHA-256 del PDF (integridad) y lo envía en el alta/edición.
import { apiFetch } from "./client";
import { fetchLegalRepresentatives } from "./admin-legal-representatives";

/**
 * Escritura proyectada para la gestión admin (metadatos, sin binario). `representedCompanyIds` es la
 * multi-selección de compañías registradas a las que aplica la escritura; `storageSha256` es la
 * integridad del PDF custodiado en storage.
 */
export interface DeedItem {
  id: string;
  description: string;
  storagePath: string;
  storageSha256: string;
  /** Vigencia (YYYY-MM-DD). */
  vigenciaDesde: string;
  vigenciaHasta: string;
  isActive: boolean;
  representedCompanyIds: string[];
  createdAt: string;
  updatedAt?: string | null;
}

/** Página de escrituras: `{ data, totalCount, page, pageSize }`. */
export interface DeedPage {
  data: DeedItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

/** Detalle de una escritura: metadatos + la presigned URL de vista inline del PDF (vida corta). */
export interface DeedDetail {
  deed: DeedItem;
  viewUrl?: string | null;
  viewUrlExpiresAt?: string | null;
}

/**
 * Presigned POST policy para subir el PDF DIRECTO a S3. `fields` van ANTES del `file` en el
 * multipart; `storagePath` es la referencia que el backend ya persistió en la escritura.
 */
export interface DeedUploadTicket {
  storagePath: string;
  url: string;
  fields: Record<string, string>;
}

/** Respuesta del alta (201) / edición (200): id + presigned upload (null si no se reemplaza el PDF). */
export interface DeedSaved {
  id: string;
  upload?: DeedUploadTicket | null;
}

/** Cuerpo del alta/edición (POST/PUT). `sha256` null en edición conserva el PDF actual. */
export interface DeedRequest {
  description: string;
  vigenciaDesde: string;
  vigenciaHasta: string;
  sha256: string | null;
  representedCompanyIds: string[];
  /**
   * Representante que asocia la escritura (Feature #10929). Solo se envía en el ALTA (desde el detalle
   * del representante); en la edición se omite y el backend conserva el representante actual.
   */
  representativeId?: string;
}

/**
 * Datos del formulario de escritura. En alta el PDF es obligatorio; en edición es opcional (si no se
 * elige uno nuevo, se conserva el custodiado).
 */
export interface DeedFormInput {
  description: string;
  vigenciaDesde: string;
  vigenciaHasta: string;
  companyIds: string[];
  file: File | null;
}

/** Compañía registrada seleccionable (derivada del directorio de representantes, distinct por NIT). */
export interface RepresentedCompany {
  /** `representedCompanyId` del directorio: el id que la escritura persiste. */
  id: string;
  /** NIT de la compañía (PII, Ley 1581). */
  nit: string;
  name: string;
}

function base(tenantId: string): string {
  return `/api/v1/admin/companies/${tenantId}/deeds`;
}

/** SHA-256 hex (minúsculas) del PDF, calculado en el navegador (integridad del artefacto). */
async function sha256Hex(file: File): Promise<string> {
  const buffer = await file.arrayBuffer();
  const digest = await crypto.subtle.digest("SHA-256", buffer);
  return Array.from(new Uint8Array(digest))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
}

/** GET "" — página de escrituras de la compañía. */
export function fetchDeeds(
  tenantId: string,
  page: number,
  pageSize: number,
  signal?: AbortSignal,
): Promise<DeedPage> {
  return apiFetch<DeedPage>(base(tenantId), { query: { page, pageSize }, signal });
}

/** GET "/{id}" — detalle + presigned URL de vista inline del PDF. */
export function fetchDeedDetail(
  tenantId: string,
  id: string,
  signal?: AbortSignal,
): Promise<DeedDetail> {
  return apiFetch<DeedDetail>(`${base(tenantId)}/${id}`, { signal });
}

/** POST "" — alta de escritura. Devuelve id + presigned upload. Lanza ApiValidationError en 422. */
export function createDeed(tenantId: string, body: DeedRequest): Promise<DeedSaved> {
  return apiFetch<DeedSaved>(base(tenantId), { method: "POST", body });
}

/** PUT "/{id}" — edición. Devuelve id + presigned upload (null si no se reemplaza el PDF). */
export function updateDeed(tenantId: string, id: string, body: DeedRequest): Promise<DeedSaved> {
  return apiFetch<DeedSaved>(`${base(tenantId)}/${id}`, { method: "PUT", body });
}

/** DELETE "/{id}" — baja lógica idempotente (204). */
export function deleteDeed(tenantId: string, id: string): Promise<void> {
  return apiFetch<void>(`${base(tenantId)}/${id}`, { method: "DELETE" });
}

/**
 * Sube el PDF DIRECTO a S3 con la presigned POST policy: los campos firmados van ANTES del `file`. No
 * se fija Content-Type ni Authorization (es S3, no el API; el navegador pone el boundary del multipart).
 */
export async function uploadDeedPdf(upload: DeedUploadTicket, file: File): Promise<void> {
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
 * Orquesta el guardado de una escritura (alta o edición):
 *  1) calcula el SHA-256 del PDF elegido (si hay uno),
 *  2) crea/edita los metadatos (el backend devuelve la presigned upload),
 *  3) sube el PDF DIRECTO a S3 si se eligió uno nuevo.
 * Propaga ApiValidationError (422) para el mapeo por campo en el formulario.
 */
export async function saveDeed(
  tenantId: string,
  editingId: string | null,
  input: DeedFormInput,
  representativeId?: string,
): Promise<DeedSaved> {
  const sha256 = input.file ? await sha256Hex(input.file) : null;
  const body: DeedRequest = {
    description: input.description,
    vigenciaDesde: input.vigenciaDesde,
    vigenciaHasta: input.vigenciaHasta,
    sha256,
    representedCompanyIds: input.companyIds,
    // Feature #10929: en el alta se asocia al representante del detalle; en la edición no se envía
    // (el backend conserva el representante actual).
    ...(editingId ? {} : { representativeId }),
  };
  const saved = editingId
    ? await updateDeed(tenantId, editingId, body)
    : await createDeed(tenantId, body);

  if (input.file && saved.upload) {
    await uploadDeedPdf(saved.upload, input.file);
  }
  return saved;
}

/**
 * Compañías seleccionables para una escritura: las mismas registradas con los representantes legales
 * (HU #10904), distinct por NIT. No hay endpoint de compañías: se derivan del directorio de
 * representantes (`representedCompanyId` + NIT + nombre). Trae una página amplia (las gestoras
 * representan pocas compañías) y deduplica por NIT preservando el orden de aparición.
 */
export async function fetchRepresentedCompanies(
  tenantId: string,
  signal?: AbortSignal,
): Promise<RepresentedCompany[]> {
  const reps = await fetchLegalRepresentatives(tenantId, 1, 200, signal);
  const byNit = new Map<string, RepresentedCompany>();
  for (const r of reps.data) {
    if (!byNit.has(r.companyDocumentNumber)) {
      byNit.set(r.companyDocumentNumber, {
        id: r.representedCompanyId,
        nit: r.companyDocumentNumber,
        name: r.companyName,
      });
    }
  }
  return Array.from(byNit.values());
}
