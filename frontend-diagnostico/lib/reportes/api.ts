// Descargas del tablero (HU #13851, F07-H6). Contratos de contracts/openapi/diagnostico.v1.yaml (tag Reportes):
// GET /diagnosticos/{id}/vehiculos/{vid}/pdf (#13848), GET /diagnosticos/{id}/excel (#13849) y el ZIP en segundo plano
// (#13850): POST /diagnosticos/{id}/evidencias {tipo:"ZIP"} → GET …/evidencias/{eid} hasta LISTA o ERROR → GET …/archivo.
// Todo pasa por el BFF (/api/v1/*), que reenvía el cuerpo binario y el Content-Disposition. La API vuelve a exigir
// `diagnostico.reportes.descargar` y la regla de emisión (EmisionPdf.Puede); el frontend solo la anticipa.
import { isReauthenticating, reauthenticate } from "@flit/auth/client";
import { API_PREFIX, ApiError, apiFetch } from "@/lib/api";
import type { EstadoDiagnostico } from "@/lib/diagnosticos/api";
import type { EstadoCelda, FilaVehiculo } from "@/lib/tablero/api";

export type EstadoZip = "PENDIENTE" | "GENERANDO" | "LISTA" | "ERROR";

/** Cuerpo del 200/202 del POST y del GET de estado (schema `EstadoEvidencia`). */
export interface EstadoEvidencia {
  id: string;
  tipo: "ZIP";
  estado: EstadoZip;
  /** Vehículos con PDF (esperados mientras está en cola; los del archivo cuando LISTA). */
  vehiculos_incluidos: number;
  /** Vehículos fuera por no haber terminado (salen en el índice del ZIP). */
  vehiculos_pendientes: number;
  creado_en: string;
  generado_en: string | null;
  /** Solo con ERROR; es técnico y no se muestra. */
  tipo_error: string | null;
}

/** Cada cuánto se lee el estado del ZIP mientras está en cola o generándose. */
export const INTERVALO_SONDEO_ZIP_MS = 3000;

const ruta = (id: string, sufijo: string) => `/diagnosticos/${encodeURIComponent(id)}${sufijo}`;

/** URL del BFF del PDF de un vehículo (para el `href` del enlace con `download`). */
export const urlPdfVehiculo = (id: string, vid: string): string => `${API_PREFIX}${ruta(id, `/vehiculos/${encodeURIComponent(vid)}/pdf`)}`;

/** URL del BFF del Excel del lote. */
export const urlExcelLote = (id: string): string => `${API_PREFIX}${ruta(id, "/excel")}`;

/** URL del BFF del archivo de un ZIP LISTA. */
export const urlArchivoZip = (id: string, eid: string): string => `${API_PREFIX}${ruta(id, `/evidencias/${encodeURIComponent(eid)}/archivo`)}`;

/** POST /diagnosticos/{id}/evidencias: 202 (en cola, o el mismo pedido en curso) o 200 (ya hay uno LISTA igual). */
export function pedirZip(id: string): Promise<EstadoEvidencia> {
  return apiFetch<EstadoEvidencia>(ruta(id, "/evidencias"), { method: "POST", body: { tipo: "ZIP" } });
}

/** GET /diagnosticos/{id}/evidencias/{eid}: estado del ZIP pedido. */
export function estadoZip(id: string, eid: string, signal?: AbortSignal): Promise<EstadoEvidencia> {
  return apiFetch<EstadoEvidencia>(ruta(id, `/evidencias/${encodeURIComponent(eid)}`), { signal });
}

/** Nombre del archivo del Content-Disposition: `filename*=UTF-8''…` y si no `filename=…`; si no, el de por defecto. */
export function nombreArchivo(contentDisposition: string | null, porDefecto: string): string {
  if (!contentDisposition) return porDefecto;
  const extendido = /filename\*\s*=\s*UTF-8''([^;]+)/i.exec(contentDisposition);
  if (extendido) {
    try {
      return decodeURIComponent(extendido[1].trim());
    } catch {
      // Codificación inválida: se intenta con el nombre simple.
    }
  }
  const simple = /filename\s*=\s*"?([^";]+)"?/i.exec(contentDisposition);
  return simple?.[1].trim() || porDefecto;
}

/** Entrega el blob al navegador como descarga con su nombre. */
function guardar(blob: Blob, nombre: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = nombre;
  a.rel = "noopener";
  document.body.appendChild(a);
  a.click();
  a.remove();
  // Se libera después del clic: algunos navegadores leen la URL en el siguiente ciclo.
  setTimeout(() => URL.revokeObjectURL(url), 0);
}

/**
 * Descarga un archivo del BFF (PDF, Excel o ZIP) por blob, con el nombre del Content-Disposition. Un error HTTP lanza
 * `ApiError` con el `code` del cuerpo (409 VEHICULO_NO_TERMINADO, 404 NO_ENCONTRADO…) y no descarga nada.
 */
export async function descargarArchivo(url: string, porDefecto: string): Promise<string> {
  if (isReauthenticating()) return new Promise<string>(() => {});
  const respuesta = await fetch(url, { method: "GET", cache: "no-store" });
  if (!respuesta.ok) {
    if (respuesta.status === 401) reauthenticate({ silent: true });
    const datos = await leerError(respuesta);
    throw new ApiError(respuesta.status, datos?.error ?? "No se pudo descargar el archivo.", datos?.code);
  }
  const nombre = nombreArchivo(respuesta.headers.get("Content-Disposition"), porDefecto);
  guardar(await respuesta.blob(), nombre);
  return nombre;
}

async function leerError(respuesta: Response): Promise<{ code?: string; error?: string } | null> {
  try {
    const texto = await respuesta.text();
    return texto ? (JSON.parse(texto) as { code?: string; error?: string }) : null;
  } catch {
    return null;
  }
}

const MENSAJES: Record<string, string> = {
  VEHICULO_NO_TERMINADO: "Este vehículo todavía tiene consultas en curso. El PDF estará disponible cuando terminen.",
  SIN_VEHICULOS_TERMINADOS: "Aún no hay vehículos terminados para armar el ZIP. Inténtalo cuando termine alguno.",
  LOTE_SIN_LANZAR: "El lote todavía no se ha lanzado: aún no hay nada que descargar.",
  EVIDENCIA_NO_LISTA: "El ZIP todavía se está preparando. Espera a que quede listo.",
};

/** Texto para el usuario de un error de descarga (nunca el código técnico). */
export function mensajeDescarga(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.code && MENSAJES[error.code]) return MENSAJES[error.code];
    if (error.status === 404) return "No encontramos este diagnóstico o ya no tienes acceso a él.";
    if (error.status === 403) return "No tienes permiso para descargar reportes.";
  }
  return error instanceof Error ? error.message : "No se pudo descargar el archivo.";
}

const CERRADAS: ReadonlySet<EstadoCelda> = new Set(["COMPLETADO", "NA", "ERROR"]);

/**
 * Misma regla que `EmisionPdf.Puede` del backend (R-7/Q7): el vehículo terminó (todas sus celdas en COMPLETADO o N/A,
 * `terminado` de la grilla) o el lote está INCOMPLETO y todas sus celdas están cerradas (las en ERROR salen «consulta no
 * disponible»). Sin celdas, no. El servidor decide: si responde 409, se muestra su mensaje.
 */
export function puedeEmitirPdf(estadoLote: EstadoDiagnostico, vehiculo: FilaVehiculo): boolean {
  if (vehiculo.terminado) return true;
  return estadoLote === "INCOMPLETO" && vehiculo.celdas.length > 0 && vehiculo.celdas.every((c) => CERRADAS.has(c.estado));
}
