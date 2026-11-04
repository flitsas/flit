// HU #14076 (Feature #14070, Épica #14021) — cliente tipado del CONTENIDO administrable del portal de Persona Natural
// en Plataforma (HU #14073, solo SuperAdmin). Contrato real leído de `AdminPlataformaPersonaNaturalEndpoints.Contenido.cs`:
//   preguntas: GET/POST …/contenido/preguntas · PUT …/preguntas/{id} · PATCH …/preguntas/{id}/activa · PUT …/preguntas/orden
//   banners:   GET/POST(multipart) …/contenido/banners · PUT(multipart) …/banners/{id} · PATCH …/banners/{id}/activo ·
//              PUT …/banners/orden · GET …/banners/{id}/imagen (requiere Authorization: se descarga como blob)
//   enlaces:   GET …/contenido/enlaces · PUT …/enlaces/{codigo} { url }  (url vacía = «sin documento oficial»)
//   publicar:  GET …/contenido/borrador · POST …/contenido/publicar (409 `sin_cambios_por_publicar`) · GET …/contenido/versiones
// Todo lo que se edita es el BORRADOR; el portal solo ve lo publicado. El texto es plano: no se acepta HTML.
import { apiFetch, friendlyErrorMessage, getToken, resolveApiUrl } from "./client";
import { PN_BASE } from "./admin-pn-tarifas";
import { ApiError } from "./types";

const BASE = `${PN_BASE}/contenido`;

export const PREGUNTA_MAX = 300;
export const RESPUESTA_MAX = 2000;
export const BANNER_TITULO_MAX = 200;
export const BANNER_TEXTO_ALTERNATIVO_MAX = 250;
export const ENLACE_MAX = 2048;
export const BANNER_IMAGEN_MAX_BYTES = 2 * 1024 * 1024;
export const BANNER_IMAGEN_TIPOS = ["image/png", "image/jpeg", "image/webp"] as const;

export interface FaqPn {
  id: string;
  pregunta: string;
  respuesta: string;
  orden: number;
  activa: boolean;
  rowVersion: number;
  creadaEn: string;
  actualizadaEn: string | null;
}

export interface BannerPn {
  id: string;
  titulo: string;
  textoAlternativo: string;
  enlaceUrl: string | null;
  orden: number;
  vigenteDesde: string | null;
  vigenteHasta: string | null;
  activo: boolean;
  imagenSha256: string;
  imagenContentType: string;
  imagenBytes: number;
  rowVersion: number;
  creadoEn: string;
  actualizadoEn: string | null;
}

export type CodigoEnlace = "POLITICA_DATOS" | "TERMINOS" | "CONTACTO_VENTAS" | "VIDEO";
export const CODIGOS_ENLACE: readonly CodigoEnlace[] = ["POLITICA_DATOS", "TERMINOS", "CONTACTO_VENTAS", "VIDEO"];
export const ENLACE_LABEL: Record<CodigoEnlace, string> = {
  POLITICA_DATOS: "Política de tratamiento de datos",
  TERMINOS: "Términos y condiciones",
  CONTACTO_VENTAS: "Contacto de ventas",
  VIDEO: "Video explicativo",
};
/** Los dos legales conservan el aviso actual del portal mientras no tengan documento oficial. */
export const ENLACES_LEGALES: readonly CodigoEnlace[] = ["POLITICA_DATOS", "TERMINOS"];

export interface EnlacePn {
  codigo: CodigoEnlace;
  /** `null` = sin documento oficial / sin valor. */
  url: string | null;
  actualizadoEn: string | null;
}

export interface ContenidoPn {
  preguntas: { id: string; pregunta: string; respuesta: string; orden: number }[];
  banners: {
    id: string;
    titulo: string;
    textoAlternativo: string;
    enlaceUrl: string | null;
    orden: number;
    vigenteDesde: string | null;
    vigenteHasta: string | null;
    imagenSha256: string;
    imagenContentType: string;
  }[];
  enlaces: EnlacePn[];
}

export interface BorradorContenidoPn {
  contenido: ContenidoPn;
  versionPublicada: number | null;
  hayCambiosSinPublicar: boolean;
}

export interface VersionContenidoPn {
  version: number;
  publicadoEn: string;
  publicadoPor: string | null;
  contenido: ContenidoPn;
}

export interface ResumenVersionContenidoPn {
  version: number;
  publicadoEn: string;
  publicadoPor: string | null;
  preguntas: number;
  banners: number;
}

// ── Preguntas frecuentes ─────────────────────────────────────────────────────────────────────────────────────────

export interface GuardarFaq {
  pregunta: string;
  respuesta: string;
  activa?: boolean;
}

export const fetchFaqsPn = (signal?: AbortSignal) => apiFetch<FaqPn[]>(`${BASE}/preguntas`, { signal });
export const crearFaqPn = (body: GuardarFaq) => apiFetch<FaqPn>(`${BASE}/preguntas`, { method: "POST", body });
export const actualizarFaqPn = (id: string, body: GuardarFaq) =>
  apiFetch<FaqPn>(`${BASE}/preguntas/${id}`, { method: "PUT", body });
export const establecerFaqActivaPn = (id: string, activa: boolean) =>
  apiFetch<FaqPn>(`${BASE}/preguntas/${id}/activa`, { method: "PATCH", body: { activa } });
export const ordenarFaqsPn = (ids: string[]) => apiFetch<void>(`${BASE}/preguntas/orden`, { method: "PUT", body: { ids } });

// ── Banners ──────────────────────────────────────────────────────────────────────────────────────────────────────

/** Datos del formulario: `vigenteDesde/Hasta` llegan como `yyyy-MM-ddTHH:mm` en hora de Colombia (o vacío). */
export interface GuardarBanner {
  titulo: string;
  textoAlternativo: string;
  enlaceUrl: string;
  vigenteDesde: string;
  vigenteHasta: string;
  activo: boolean;
  /** Obligatoria al crear; al editar, `null` conserva la imagen actual. */
  archivo: File | null;
}

const COLOMBIA_UTC_OFFSET_HOURS = 5;

/** `yyyy-MM-ddTHH:mm` (hora Colombia) → ISO UTC. Aritmética pura: no usa el huso horario del navegador. */
export function colombiaLocalAUtcIso(valor: string): string {
  return new Date(Date.parse(`${valor}:00.000Z`) + COLOMBIA_UTC_OFFSET_HOURS * 3_600_000).toISOString();
}

/** ISO UTC → `yyyy-MM-ddTHH:mm` en hora Colombia, para precargar un `<input type="datetime-local">`. */
export function utcIsoAColombiaLocal(iso: string | null): string {
  if (!iso) return "";
  return new Date(Date.parse(iso) - COLOMBIA_UTC_OFFSET_HOURS * 3_600_000).toISOString().slice(0, 16);
}

function bannerFormData(input: GuardarBanner): FormData {
  const form = new FormData();
  form.append("titulo", input.titulo.trim());
  form.append("textoAlternativo", input.textoAlternativo.trim());
  if (input.enlaceUrl.trim()) form.append("enlaceUrl", input.enlaceUrl.trim());
  if (input.vigenteDesde) form.append("vigenteDesde", colombiaLocalAUtcIso(input.vigenteDesde));
  if (input.vigenteHasta) form.append("vigenteHasta", colombiaLocalAUtcIso(input.vigenteHasta));
  form.append("activo", String(input.activo));
  if (input.archivo) form.append("file", input.archivo);
  return form;
}

async function enviarMultipart(path: string, method: "POST" | "PUT", form: FormData): Promise<BannerPn> {
  const token = getToken();
  const response = await fetch(resolveApiUrl(path), {
    method,
    headers: token ? { Authorization: `Bearer ${token}` } : {},
    body: form,
  });
  if (!response.ok) {
    let detalle: unknown = null;
    try {
      detalle = await response.json();
    } catch {
      detalle = null;
    }
    throw new ApiError(response.status, friendlyErrorMessage(detalle as Record<string, unknown> | null), detalle);
  }
  return (await response.json()) as BannerPn;
}

export const fetchBannersPn = (signal?: AbortSignal) => apiFetch<BannerPn[]>(`${BASE}/banners`, { signal });
export const crearBannerPn = (input: GuardarBanner) => enviarMultipart(`${BASE}/banners`, "POST", bannerFormData(input));
export const actualizarBannerPn = (id: string, input: GuardarBanner) =>
  enviarMultipart(`${BASE}/banners/${id}`, "PUT", bannerFormData(input));
export const establecerBannerActivoPn = (id: string, activo: boolean) =>
  apiFetch<BannerPn>(`${BASE}/banners/${id}/activo`, { method: "PATCH", body: { activo } });
export const ordenarBannersPn = (ids: string[]) => apiFetch<void>(`${BASE}/banners/orden`, { method: "PUT", body: { ids } });

/**
 * Descarga la imagen del banner del borrador como blob (el endpoint exige `Authorization`, así que un `<img src>`
 * directo no sirve) y devuelve una URL de objeto. Quien la use debe liberarla con `URL.revokeObjectURL`.
 */
export async function fetchImagenBannerPn(id: string, signal?: AbortSignal): Promise<string> {
  const token = getToken();
  const response = await fetch(resolveApiUrl(`${BASE}/banners/${id}/imagen`), {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
    signal,
  });
  if (!response.ok) throw new ApiError(response.status, "No se pudo cargar la imagen del banner.");
  return URL.createObjectURL(await response.blob());
}

// ── Enlaces, borrador y publicación ──────────────────────────────────────────────────────────────────────────────

export const fetchEnlacesPn = (signal?: AbortSignal) => apiFetch<EnlacePn[]>(`${BASE}/enlaces`, { signal });
/** `url` vacía o `null` = «sin documento oficial»: la API no inventa texto legal. */
export const establecerEnlacePn = (codigo: CodigoEnlace, url: string | null) =>
  apiFetch<EnlacePn>(`${BASE}/enlaces/${codigo}`, { method: "PUT", body: { url } });

export const fetchBorradorPn = (signal?: AbortSignal) => apiFetch<BorradorContenidoPn>(`${BASE}/borrador`, { signal });
export const publicarContenidoPn = () => apiFetch<VersionContenidoPn>(`${BASE}/publicar`, { method: "POST" });
export const fetchVersionesPn = (signal?: AbortSignal) =>
  apiFetch<ResumenVersionContenidoPn[]>(`${BASE}/versiones`, { signal });
