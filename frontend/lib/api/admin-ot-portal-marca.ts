// Cliente tipado de la marca del portal Persona Natural del organismo (Épica #14297, Feature #14374, HU #14377).
// Contrato: docs/design/EPICA-14297-contrato-portal-ot.md → grupo `/api/v1/admin/ot/portal-marca`.
// Mismo alcance que `admin-ot*.ts`: el `ot_admin` trabaja con el organismo de su token (ignora `transitOfficeId`) y el
// SuperAdmin lo indica con `?transitOfficeId=`. Los errores del contrato son `{ code, message }` con el motivo exacto en
// español: aquí se conservan tal cual (`apiFetch` perdería el `message` de los 422).
import { clearToken, emitSessionExpired } from "@/lib/auth/session";
import { getToken, resolveApiUrl } from "./client";
import { ApiError } from "./types";

const base = "/api/v1/admin/ot/portal-marca";

export interface PortalMarcaScope {
  transitOfficeId?: string;
}

export interface PortalMarcaColors {
  primary: string;
  secondary: string;
  onPrimary: string;
}

export interface PortalMarcaSnapshot {
  platformName: string | null;
  colors: PortalMarcaColors | null;
  logoId: string | null;
  bannerId: string | null;
}

export interface PortalMarca {
  transitOfficeId: string;
  draft: PortalMarcaSnapshot;
  published: PortalMarcaSnapshot | null;
  publishedVersion: number;
  publishedAt: string | null;
  publishedBy: { userId: string } | null;
  hasUnpublishedChanges: boolean;
  /** Imagen del borrador. Ruta de la API que exige sesión: se pide con `fetchPortalMarcaImage`, no con `<img src>`. */
  logoUrl: string | null;
  bannerUrl: string | null;
  completeness: { isComplete: boolean; missing: string[] };
  rowVersion: number;
}

export interface SavePortalMarcaDraftRequest {
  platformName: string | null;
  colors: PortalMarcaColors;
  rowVersion: number;
}

/** Códigos de error del contrato. */
export type PortalMarcaErrorCode =
  | "PORTAL_MARCA_CONTRASTE"
  | "PORTAL_MARCA_INCOMPLETA"
  | "PORTAL_MARCA_ARCHIVO_INVALIDO"
  | "PORTAL_MARCA_CONFLICTO";

const GENERIC = "No se pudo completar la solicitud. Inténtalo de nuevo.";

function withScope(path: string, scope?: PortalMarcaScope): string {
  if (!scope?.transitOfficeId) return path;
  const url = new URL(path, "http://relative.invalid");
  if (!url.searchParams.has("transitOfficeId")) url.searchParams.set("transitOfficeId", scope.transitOfficeId);
  return `${url.pathname}${url.search}`;
}

async function readBody(response: Response): Promise<{ code?: unknown; message?: unknown } | null> {
  try {
    return (await response.json()) as { code?: unknown; message?: unknown } | null;
  } catch {
    return null;
  }
}

/** Motivo exacto que manda el servidor (`message`); sin él, un texto genérico que no filtra ruta ni status. */
async function toApiError(response: Response): Promise<ApiError> {
  const body = await readBody(response);
  if (response.status === 401) {
    clearToken();
    emitSessionExpired();
    return new ApiError(401, "Tu sesión expiró. Vuelve a iniciar sesión.", body);
  }
  const message = typeof body?.message === "string" && body.message.trim() ? body.message : GENERIC;
  return new ApiError(response.status, message, body);
}

async function request<T>(
  method: "GET" | "PUT" | "POST" | "DELETE",
  path: string,
  scope: PortalMarcaScope | undefined,
  init: { json?: unknown; form?: FormData; signal?: AbortSignal } = {},
): Promise<T> {
  const headers: Record<string, string> = {};
  const token = getToken();
  if (token) headers.Authorization = `Bearer ${token}`;
  if (init.json !== undefined) headers["Content-Type"] = "application/json";

  let response: Response;
  try {
    response = await fetch(resolveApiUrl(withScope(path, scope)), {
      method,
      headers,
      body: init.form ?? (init.json === undefined ? undefined : JSON.stringify(init.json)),
      signal: init.signal,
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") throw error;
    throw new ApiError(0, "No se pudo conectar con el servidor. Revisa tu conexión e inténtalo de nuevo.");
  }
  if (!response.ok) throw await toApiError(response);
  return (await response.json()) as T;
}

/** GET — marca del organismo (borrador, publicada y faltantes). */
export function getPortalMarca(scope?: PortalMarcaScope, signal?: AbortSignal): Promise<PortalMarca> {
  return request<PortalMarca>("GET", base, scope, { signal });
}

/** PUT — guarda el borrador (nombre y colores). `rowVersion` evita pisar un cambio ajeno (409 `PORTAL_MARCA_CONFLICTO`). */
export function savePortalMarcaDraft(body: SavePortalMarcaDraftRequest, scope?: PortalMarcaScope): Promise<PortalMarca> {
  return request<PortalMarca>("PUT", base, scope, { json: body });
}

function imageForm(file: File): FormData {
  const form = new FormData();
  form.append("file", file);
  return form;
}

/** POST /logo — multipart; el logo queda en el borrador. */
export function uploadPortalMarcaLogo(file: File, scope?: PortalMarcaScope): Promise<PortalMarca> {
  return request<PortalMarca>("POST", `${base}/logo`, scope, { form: imageForm(file) });
}

/** DELETE /logo — el portal usa el logo FLIT cuando se publique. */
export function deletePortalMarcaLogo(scope?: PortalMarcaScope): Promise<PortalMarca> {
  return request<PortalMarca>("DELETE", `${base}/logo`, scope);
}

/** POST /banner — multipart; el banner queda en el borrador. */
export function uploadPortalMarcaBanner(file: File, scope?: PortalMarcaScope): Promise<PortalMarca> {
  return request<PortalMarca>("POST", `${base}/banner`, scope, { form: imageForm(file) });
}

/** DELETE /banner — el portal usa el banner FLIT cuando se publique. */
export function deletePortalMarcaBanner(scope?: PortalMarcaScope): Promise<PortalMarca> {
  return request<PortalMarca>("DELETE", `${base}/banner`, scope);
}

/** POST /publish — el servidor revalida nombre, colores y contraste 4,5:1. */
export function publishPortalMarca(rowVersion: number, scope?: PortalMarcaScope): Promise<PortalMarca> {
  return request<PortalMarca>("POST", `${base}/publish`, scope, { json: { rowVersion } });
}

/** POST /retire — el organismo vuelve a la marca FLIT; el borrador se conserva. */
export function retirePortalMarca(rowVersion: number, scope?: PortalMarcaScope): Promise<PortalMarca> {
  return request<PortalMarca>("POST", `${base}/retire`, scope, { json: { rowVersion } });
}

/** Código del contrato de un error (`PORTAL_MARCA_*`), si lo trae. */
export function portalMarcaErrorCode(error: unknown): string | null {
  if (!(error instanceof ApiError)) return null;
  const code = (error.body as { code?: unknown } | null | undefined)?.code;
  return typeof code === "string" ? code : null;
}

/** Motivo para mostrar: el `message` del servidor, o el de la excepción si no vino de la API. */
export function portalMarcaErrorMessage(error: unknown, fallback = GENERIC): string {
  if (error instanceof Error && error.message.trim()) return error.message;
  return fallback;
}

/**
 * Las imágenes del borrador exigen sesión, y `<img src>` no manda el token: se piden con `Authorization` y se
 * entregan como URL de objeto (el llamador la libera con `URL.revokeObjectURL`).
 */
export async function fetchPortalMarcaImage(path: string, scope?: PortalMarcaScope, signal?: AbortSignal): Promise<string> {
  const headers: Record<string, string> = {};
  const token = getToken();
  if (token) headers.Authorization = `Bearer ${token}`;
  const response = await fetch(resolveApiUrl(withScope(path, scope)), { headers, signal });
  if (!response.ok) throw await toApiError(response);
  return URL.createObjectURL(await response.blob());
}
