// Cliente tipado de banners promocionales admin (HU #12241, Feature #12236). Contrato REAL leído
// de `AdminBannersEndpoints.cs`: a diferencia de escrituras/documentos personalizados, NO hay
// presigned upload a storage — el archivo viaja DIRECTO en el mismo POST/PUT como
// `multipart/form-data`. `apiFetch` es JSON-only, así que create/update usan `fetch` directo con
// `FormData` (mismo patrón que `adjuntarOtLicenciaTransito` en `admin-ot.ts`).
import { apiFetch, friendlyErrorMessage, getToken, resolveApiUrl } from "./client";
import { ApiError } from "./types";

export type BannerEstado = "programado" | "activo" | "inactivo" | "expirado";

/** `BannerResponse` del backend (camelCase). */
export interface Banner {
  id: string;
  name: string;
  /**
   * URL cruda que manda el backend. NO USAR DIRECTO: hasta HU #12241 traía el prefijo
   * `/api/v1` recortado (`/public/banners/{id}/image` en vez de `/api/v1/public/banners/{id}/image`).
   * Se corrigió en `BannerResponse.BuildImageUrl`, pero el frontend arma la URL con
   * `bannerImageUrl(id)` para no volver a depender de que el backend nunca se desalinee.
   */
  imageUrl: string;
  imageSha256: string;
  linkUrl: string | null;
  validFrom: string | null;
  validUntil: string | null;
  isActive: boolean;
  estado: BannerEstado;
  createdAt: string;
  updatedAt: string | null;
  rowVersion: number;
}

export interface BannerPagedResult {
  data: Banner[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface BannerListParams {
  page?: number;
  pageSize?: number;
  includeDeleted?: boolean;
}

const base = "/api/v1/admin/banners";

/**
 * FLIT opera en hora Colombia (UTC-5, sin horario de verano) — fijo, sin selector de zona
 * horaria (Bug #12584, defecto 1: el formulario solo permitía elegir día, nunca hora de
 * inicio/fin de vigencia). La conversión es aritmética pura (no usa el huso horario del
 * navegador de quien administra el banner, que puede no estar en Colombia).
 */
const COLOMBIA_UTC_OFFSET_HOURS = 5;

/** `yyyy-MM-ddTHH:mm` (hora Colombia, valor nativo de `<input type="datetime-local">`) a ISO UTC. */
function colombiaLocalToUtcIso(value: string): string {
  const utcMs = Date.parse(`${value}:00.000Z`) + COLOMBIA_UTC_OFFSET_HOURS * 3_600_000;
  return new Date(utcMs).toISOString();
}

/** ISO UTC del backend a `yyyy-MM-ddTHH:mm` en hora Colombia, para precargar el `<input>`. */
export function bannerDateTimeInputValue(iso: string | null): string {
  if (!iso) return "";
  const localMs = Date.parse(iso) - COLOMBIA_UTC_OFFSET_HOURS * 3_600_000;
  return new Date(localMs).toISOString().slice(0, 16);
}

/**
 * URL pública de la imagen del banner, construida SIEMPRE a partir del `id` (nunca desde
 * `Banner.imageUrl` crudo — ver el aviso en el campo). Coincide con la ruta montada en
 * `PublicBannersEndpoints.cs`: `GET /api/v1/public/banners/{id}/image`.
 */
export function bannerImageUrl(id: string): string {
  return resolveApiUrl(`/api/v1/public/banners/${id}/image`);
}

/** GET "" — listado paginado de banners (AC1). */
export function fetchBanners(
  params: BannerListParams = {},
  signal?: AbortSignal,
): Promise<BannerPagedResult> {
  return apiFetch<BannerPagedResult>(base, {
    query: {
      page: params.page,
      pageSize: params.pageSize,
      includeDeleted: params.includeDeleted,
    },
    signal,
  });
}

/** Datos del formulario de alta/edición (AC2). En alta la imagen es obligatoria; en edición es
 * opcional (si no se elige una nueva, el backend conserva la custodiada). */
export interface BannerFormInput {
  name: string;
  /** Enlace opcional; cadena vacía = sin enlace. */
  linkUrl: string;
  /** `yyyy-MM-ddTHH:mm` en hora Colombia (valor nativo de `<input type="datetime-local">`) o
   * cadena vacía = sin vigencia. */
  validFrom: string;
  /** `yyyy-MM-ddTHH:mm` en hora Colombia o cadena vacía = sin vigencia. */
  validUntil: string;
  file: File | null;
  /**
   * `POST`/`PUT` de `AdminBannersEndpoints.cs` NO aceptan este campo — el backend siempre crea el
   * banner activo y, al editar, conserva el estado que ya tenía. `createBanner`/`updateBanner` lo
   * aplican aparte con el `PATCH /{id}/active` dedicado cuando difiere, para que el formulario
   * (alta o edición) pueda dejar el banner inhabilitado sin que el caller tenga que orquestar dos
   * llamadas.
   */
  isActive: boolean;
}

/**
 * Arma el `FormData` multipart. Las fechas llegan como hora Colombia (`datetime-local`) y se
 * convierten a ISO UTC (`colombiaLocalToUtcIso`) antes de enviarlas: el backend compara `now`
 * (UTC) contra `validFrom`/`validUntil` para calcular `estado` (`BannerEstadoCalculator`).
 */
function buildFormData(input: BannerFormInput): FormData {
  const form = new FormData();
  form.append("name", input.name.trim());
  if (input.linkUrl.trim()) {
    form.append("linkUrl", input.linkUrl.trim());
  }
  if (input.validFrom) {
    form.append("validFrom", colombiaLocalToUtcIso(input.validFrom));
  }
  if (input.validUntil) {
    form.append("validUntil", colombiaLocalToUtcIso(input.validUntil));
  }
  if (input.file) {
    form.append("file", input.file);
  }
  return form;
}

async function readErrorBody(response: Response): Promise<unknown> {
  try {
    return await response.json();
  } catch {
    return null;
  }
}

/** POST/PUT multipart directo al API (sin presigned storage). 422 del backend trae `{ error }`. */
async function submitMultipart(url: string, method: "POST" | "PUT", form: FormData): Promise<Banner> {
  const token = getToken();
  const response = await fetch(url, {
    method,
    headers: token ? { Authorization: `Bearer ${token}` } : {},
    body: form,
  });

  if (!response.ok) {
    const detail = await readErrorBody(response);
    throw new ApiError(response.status, friendlyErrorMessage(detail as Record<string, unknown> | null), detail);
  }

  return (await response.json()) as Banner;
}

/** POST "" — alta de banner (AC2). La imagen es obligatoria; siempre nace activa en el backend. */
export async function createBanner(input: BannerFormInput): Promise<Banner> {
  const created = await submitMultipart(resolveApiUrl(base), "POST", buildFormData(input));
  return input.isActive ? created : applyActiveState(created, false);
}

/** PUT "/{id}" — edición de banner (AC2). Si no se elige imagen nueva, conserva la actual. */
export async function updateBanner(id: string, input: BannerFormInput): Promise<Banner> {
  const updated = await submitMultipart(resolveApiUrl(`${base}/${id}`), "PUT", buildFormData(input));
  return updated.isActive === input.isActive ? updated : applyActiveState(updated, input.isActive);
}

/** PATCH "/{id}/active" — activar/desactivar sin abrir el formulario completo. */
export function setBannerActive(id: string, isActive: boolean): Promise<void> {
  return apiFetch<void>(`${base}/${id}/active`, { method: "PATCH", body: { isActive } });
}

/**
 * Aplica el PATCH de estado y refleja el resultado en el objeto ya devuelto por create/update,
 * para no forzar un segundo GET solo para refrescar `isActive` en la UI.
 *
 * El `estado` que trae `banner` se calculó en el backend ANTES de este PATCH (con el `isActive`
 * viejo) — mergear solo `isActive` sin tocar `estado` dejaba la tabla desincronizada hasta F5
 * (Bug #12584, defecto 4). Con vigencia programada `estado` no depende de `isActive`
 * (`BannerEstadoCalculator`), así que ese valor ya es correcto y se conserva tal cual; sin
 * vigencia, `estado` es un espejo directo de `isActive` (activo/inactivo) y se recalcula aquí.
 */
async function applyActiveState(banner: Banner, isActive: boolean): Promise<Banner> {
  await setBannerActive(banner.id, isActive);
  const hasVigencia = banner.validFrom !== null || banner.validUntil !== null;
  const estado: BannerEstado = hasVigencia ? banner.estado : isActive ? "activo" : "inactivo";
  return { ...banner, isActive, estado };
}

/** DELETE "/{id}?confirm=true" — baja con confirmación explícita obligatoria (AC3). */
export function deleteBanner(id: string): Promise<void> {
  return apiFetch<void>(`${base}/${id}`, { method: "DELETE", query: { confirm: true } });
}
