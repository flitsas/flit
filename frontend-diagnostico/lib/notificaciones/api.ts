import { API_PREFIX, ApiError } from "@/lib/api";
import { isReauthenticating, reauthenticate } from "@flit/auth/client";

/**
 * Campana de Diagnóstico (HU #14562 · AC50–AC54, B8 de HU #14563, ADR-0086). Tipado contra `NotificacionDto` de
 * `NotificacionesEndpoints.cs` (el OpenAPI aún no publica los paths) y diseno.md §B8: lo que el servidor puede no mandar
 * va opcional.
 * D8: no existe aviso de dictamen ajustado, por eso no está en `TipoNotificacion`.
 */
export type TipoNotificacion = "ZIP_EVIDENCIAS_LISTO" | "ZIP_EVIDENCIAS_FALLIDO" | "DERIVACION_TERMINADA" | "LOTE_TERMINADO";

export interface NotificacionDiagnostico {
  id: string;
  tipo: TipoNotificacion;
  creada_en: string;
  leida_en: string | null;
  diagnostico_id: string;
  /** Nombre del lote (join del servidor con la visibilidad PROPIOS). Único texto variable que se muestra. */
  diagnostico_nombre?: string | null;
  /** Hecho de origen: evidencia (ZIP_*), operación de derivación o transición del historial. */
  referencia_id?: string;
  /** Solo contadores y códigos (CHECK de la tabla). El texto se arma en el cliente con rótulos fijos. */
  datos?: Record<string, unknown>;
  /** Texto armado por el servidor con rótulos fijos y el nombre del lote. El título siempre es el del cliente. */
  titulo?: string;
  mensaje?: string;
  /** El `href` del servidor no se usa: la ruta se arma en el cliente (el front no tiene basePath `/diagnostico`). */
  accion?: { tipo: string; href: string };
}

export interface RespuestaNotificaciones {
  no_leidas: number;
  items: NotificacionDiagnostico[];
  siguiente: string | null;
}

/** Resultado del sondeo: `sinCambios` cuando el servidor responde 304 al If-None-Match. */
export type ResultadoSondeo = { sinCambios: true } | { sinCambios: false; datos: RespuestaNotificaciones; etag: string | null };

const RUTA = `${API_PREFIX}/notificaciones`;

/** Espera indefinida mientras el hub renueva la sesión (la página se recarga sola), como `apiFetch`. */
const enEspera = <T>() => new Promise<T>(() => {});

async function fallar(respuesta: Response): Promise<never> {
  if (respuesta.status === 401) reauthenticate({ silent: true });
  const mensaje = respuesta.status >= 500 ? "Diagnóstico no respondió. Intenta de nuevo en un momento." : "No se pudo completar la acción.";
  throw new ApiError(respuesta.status, mensaje);
}

const TIPOS: ReadonlySet<string> = new Set<TipoNotificacion>(["ZIP_EVIDENCIAS_LISTO", "ZIP_EVIDENCIAS_FALLIDO", "DERIVACION_TERMINADA", "LOTE_TERMINADO"]);

/** Borde del contrato: una respuesta sin la forma de la bandeja es un error; los ítems de tipo desconocido se descartan. */
export function validarBandeja(datos: unknown): RespuestaNotificaciones {
  const d = datos as Partial<RespuestaNotificaciones> | null;
  if (!d || typeof d !== "object" || !Array.isArray(d.items)) throw new ApiError(502, "Respuesta de avisos con forma inesperada.");
  const items = d.items.filter(
    (i): i is NotificacionDiagnostico =>
      !!i && typeof i.id === "string" && typeof i.diagnostico_id === "string" && typeof i.creada_en === "string" && TIPOS.has(i.tipo),
  );
  const noLeidas = typeof d.no_leidas === "number" && d.no_leidas >= 0 ? d.no_leidas : items.filter((i) => !i.leida_en).length;
  return { no_leidas: noLeidas, items, siguiente: typeof d.siguiente === "string" ? d.siguiente : null };
}

/** GET /notificaciones?limite=20 con If-None-Match; un 304 llega sin cuerpo. */
export async function listarNotificaciones(etag: string | null, signal?: AbortSignal): Promise<ResultadoSondeo> {
  if (isReauthenticating()) return enEspera();
  const respuesta = await fetch(`${RUTA}?limite=20`, { method: "GET", headers: etag ? { "If-None-Match": etag } : undefined, cache: "no-store", signal });
  if (respuesta.status === 304) return { sinCambios: true };
  if (!respuesta.ok) return fallar(respuesta);
  return { sinCambios: false, datos: validarBandeja(await respuesta.json().catch(() => null)), etag: respuesta.headers.get("ETag") };
}

/** POST /notificaciones/{nid}/leida → 204 (idempotente). */
export async function marcarNotificacionLeida(id: string): Promise<void> {
  if (isReauthenticating()) return enEspera();
  const respuesta = await fetch(`${RUTA}/${encodeURIComponent(id)}/leida`, { method: "POST" });
  if (!respuesta.ok) await fallar(respuesta);
}

/** POST /notificaciones/leer-todas → 204. */
export async function leerTodasNotificaciones(): Promise<void> {
  if (isReauthenticating()) return enEspera();
  const respuesta = await fetch(`${RUTA}/leer-todas`, { method: "POST" });
  if (!respuesta.ok) await fallar(respuesta);
}
