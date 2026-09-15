// Cliente tipado de la identidad de marca (HU #12412/#12413/#12414). Cubre las dos bases del
// contrato (`contracts/openapi/core-api.v1.yaml`): autogestión de la cabeza (`/company/branding`)
// y SuperAdmin sobre una cabeza puntual (`/admin/companies/{tenantId}/branding`). `retire` solo
// existe del lado admin (no hay `POST /company/branding/retire` en el contrato).
import { apiFetch, friendlyErrorMessage, getToken, resolveApiUrl } from "./client";
import { ApiError } from "./types";

/** Fuente de datos del configurador — decide la base de endpoints (HU #12414 AC6). */
export type BrandingSource = "company" | "admin";

export interface BrandColors {
  primary: string;
  secondary: string;
  onPrimary: string;
}

export interface BrandingSnapshot {
  platformName: string | null;
  colors: BrandColors | null;
  logoId: string | null;
}

export interface BrandingCompleteness {
  isComplete: boolean;
  missing: string[];
}

export interface TenantBrandingResponse {
  tenantId: string;
  draft: BrandingSnapshot;
  published: BrandingSnapshot | null;
  publishedVersion: number;
  publishedAt: string | null;
  publishedBy: { userId: string } | null;
  hasUnpublishedChanges: boolean;
  logoUrl: string | null;
  completeness: BrandingCompleteness;
  rowVersion: number;
}

export interface UpsertBrandingDraftRequest {
  platformName?: string | null;
  colors?: BrandColors | null;
  logoId?: string | null;
  rowVersion?: number | null;
}

export interface BrandLogoResponse {
  logoId: string;
  version: number;
  contentType: "image/png" | "image/jpeg" | "image/webp";
  width: number;
  height: number;
  sizeBytes: number;
  sha256: string;
  logoUrl: string;
}

function requireTenantId(source: BrandingSource, tenantId?: string): string {
  if (source === "admin") {
    if (!tenantId) {
      throw new Error("branding: se requiere tenantId cuando source=admin");
    }
    return tenantId;
  }
  return "";
}

function baseUrl(source: BrandingSource, tenantId?: string): string {
  if (source === "admin") {
    return `/api/v1/admin/companies/${requireTenantId(source, tenantId)}/branding`;
  }
  return "/api/v1/company/branding";
}

/**
 * GET .../branding (HU #12412 AC1/AC3). `404` = aún sin configuración inicial.
 * `async` a propósito: si `source="admin"` sin `tenantId` (uso incorrecto del componente),
 * `baseUrl` lanza — envolver en una función async convierte ese throw síncrono en un rechazo
 * de la promesa, consistente con el resto de la API (el caller siempre hace `.catch`/`await`).
 */
export async function getBranding(
  source: BrandingSource,
  tenantId?: string,
  signal?: AbortSignal,
): Promise<TenantBrandingResponse> {
  return apiFetch<TenantBrandingResponse>(baseUrl(source, tenantId), { signal });
}

async function readErrorBody(response: Response): Promise<unknown> {
  try {
    return await response.json();
  } catch {
    return null;
  }
}

/**
 * DESVIACIÓN DE CONTRATO (reportada, no corregida aquí — fuera del scope de esta HU):
 * `apiFetch` (`lib/api/client.ts`) trata TODO `422` como validación de modelo ASP.NET
 * (`{ detail }` de ProblemDetails, o `{ errors: [...] }`) y para cualquier otra forma lanza
 * `ApiValidationError([], 422)`, perdiendo el código. El contrato de marca (`ErrorResponse`,
 * `contracts/openapi/core-api.v1.yaml:10976`) SIEMPRE responde `{ error: "<CODE>" }` en sus 422
 * (`BRANDING_COLOR_FORMAT`, `BRANDING_CONTRAST_TOO_LOW`, `BRANDING_INCOMPLETE`, …) — AC2 exige
 * mostrar ese código con el mismo texto que la validación en cliente, así que perderlo no es
 * aceptable. Los `PUT`/`POST` de escritura de este archivo usan `requestJson` (mismo patrón que
 * `submitMultipart` de `admin-banners.ts`: cualquier `!response.ok` se traduce a `ApiError` vía
 * `friendlyErrorMessage`, sin la rama especial de 422) en vez de `apiFetch`. El `GET` sí usa
 * `apiFetch` porque su único error (404) ya pasa por la rama genérica, correcta.
 */
async function requestJson<T>(path: string, method: "PUT" | "POST", body?: unknown): Promise<T> {
  const token = getToken();
  const headers: Record<string, string> = { "Content-Type": "application/json" };
  if (token) headers.Authorization = `Bearer ${token}`;

  const response = await fetch(resolveApiUrl(path), {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  if (!response.ok) {
    const detail = await readErrorBody(response);
    throw new ApiError(response.status, friendlyErrorMessage(detail as Record<string, unknown> | null), detail);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

/** PUT .../branding — crea o reemplaza el borrador (HU #12412 AC1/AC4/AC5). */
export async function upsertBrandingDraft(
  source: BrandingSource,
  body: UpsertBrandingDraftRequest,
  tenantId?: string,
): Promise<TenantBrandingResponse> {
  return requestJson<TenantBrandingResponse>(baseUrl(source, tenantId), "PUT", body);
}

/** POST .../branding/publish (HU #12412 AC4, HU #12413 AC6). */
export async function publishBranding(
  source: BrandingSource,
  rowVersion: number,
  tenantId?: string,
): Promise<TenantBrandingResponse> {
  return requestJson<TenantBrandingResponse>(`${baseUrl(source, tenantId)}/publish`, "POST", { rowVersion });
}

/** POST /admin/companies/{tenantId}/branding/retire — solo SuperAdmin (HU #12412 AC5/AC6). */
export async function retireBranding(tenantId: string): Promise<TenantBrandingResponse> {
  return requestJson<TenantBrandingResponse>(`/api/v1/admin/companies/${tenantId}/branding/retire`, "POST");
}

/**
 * POST .../branding/logo — multipart directo (mismo patrón que `admin-banners.ts`: `apiFetch`
 * es JSON-only). La versión nueva del logotipo NO se aplica al borrador hasta un `PUT` posterior
 * con el `logoId` devuelto — así la previsualización puede usarlo sin publicarlo (AC4).
 */
export async function uploadBrandLogo(
  source: BrandingSource,
  file: File,
  tenantId?: string,
): Promise<BrandLogoResponse> {
  const form = new FormData();
  form.append("file", file);

  const token = getToken();
  const response = await fetch(resolveApiUrl(`${baseUrl(source, tenantId)}/logo`), {
    method: "POST",
    headers: token ? { Authorization: `Bearer ${token}` } : {},
    body: form,
  });

  if (!response.ok) {
    const detail = await readErrorBody(response);
    throw new ApiError(response.status, friendlyErrorMessage(detail as Record<string, unknown> | null), detail);
  }

  return (await response.json()) as BrandLogoResponse;
}

/** `true` si el error es "aún sin configuración inicial" (404 `BRANDING_NOT_FOUND`). */
export function isBrandingNotFound(error: unknown): boolean {
  return error instanceof ApiError && error.status === 404;
}

/** Código de error de marca (`BRANDING_*`, `CONCURRENCY_CONFLICT`) de un `ApiError`, si lo trae. */
export function brandingErrorCode(error: unknown): string | null {
  if (!(error instanceof ApiError)) return null;
  const body = error.body as { error?: unknown; code?: unknown } | null | undefined;
  const code = body?.error ?? body?.code;
  return typeof code === "string" ? code : null;
}
