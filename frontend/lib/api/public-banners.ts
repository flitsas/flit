// Cliente tipado del endpoint PÚBLICO de banners promocionales (HU #12240 backend / #12242
// frontend, Feature #12236). Sin auth y sin header de tenant: el set de banners activos es
// global (ADR-0058) y el contenido no es sensible (ADR-0057).
//
// No confundir con `admin-banners.ts` (HU #12241, CRUD administrable) — ese vive en otro
// worktree en paralelo y este archivo no lo toca.
import { apiFetch, resolveApiUrl } from "./client";

/** Item del listado público. El backend nunca expone `imageStoragePath` ni una URL firmada. */
export interface ActiveBanner {
  id: string;
  name: string;
  linkUrl: string | null;
}

interface ListActiveBannersResponse {
  data: ActiveBanner[];
}

const base = "/api/v1/public/banners";

/**
 * GET /api/v1/public/banners/active — banners activos y vigentes en este momento (AC1). Lista
 * vacía (nunca error) cuando no hay banners activos (AC3). Público: `apiFetch` adjunta el bearer
 * si hay sesión, pero el endpoint lo ignora (`AllowAnonymous`) — no hace falta un cliente aparte.
 */
export function getActiveBanners(signal?: AbortSignal): Promise<ActiveBanner[]> {
  return apiFetch<ListActiveBannersResponse>(`${base}/active`, { signal }).then((res) => res.data);
}

/**
 * URL directa de la imagen de un banner (streaming con ETag/Cache-Control, AC2). Se usa como
 * `<img src>` plano — nunca con `apiFetch`/fetch autenticado: los navegadores no adjuntan bearer
 * tokens a esa petición, así que no hay nada especial que hacer para mantenerla anónima (AC3).
 */
export function bannerImageUrl(bannerId: string): string {
  return resolveApiUrl(`${base}/${bannerId}/image`);
}
