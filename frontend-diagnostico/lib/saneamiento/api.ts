// Saneamiento del lado de la compañía (plan §6.1, §6.3, §6.5; HU #14577). Tipos y llamadas a mano, en snake_case, del
// contrato de saneamiento de contracts/openapi/diagnostico.v1.yaml (1.4.0, lo escribe BE-A); no hay cliente generado.
// Correcciones que mandan sobre el plan: el modo es el del contrato vigente (`ModoDiagnostico`, CON_ANALISIS; C2) y la
// fila de la grilla trae el estado del vehículo calculado con sus celdas (C1, ver lib/vehiculos/listado.ts).
// SP-15: el saneamiento que ve la compañía es un tipo propio, sin usuario_id ni referencia ni huella de la evidencia; el
// de FLIT vive en lib/backoffice/saneamientos.ts. SP-11: el panel de deuda no trae el nombre de la persona.
// Errores: ApiError con el `code` del cuerpo; `mensajeErrorSaneamiento` da el texto para el usuario.
import { isReauthenticating, reauthenticate } from "@flit/auth/client";
import { API_PREFIX, ApiError, apiFetch } from "@/lib/api";
import type { ModoDiagnostico } from "@/lib/diagnosticos/api";
import { nombreArchivo } from "@/lib/reportes/api";
import type { MotivoNa } from "@/lib/tablero/api";

export type EstadoSaneamiento = "SOLICITADO" | "EN_GESTION" | "SANEADO" | "NO_PROCEDE" | "ANULADO";
/** Los estados que la compañía ve en la grilla y en el panel: un ANULADO cuenta como «sin saneamiento». */
export type EstadoSaneamientoActivo = Exclude<EstadoSaneamiento, "ANULADO">;
export type FuenteSaneamiento = "SIMIT_VEHICULO" | "SIMIT_PROPIETARIO" | "SIMIT_LOCATARIO" | "IMPUESTO" | "SOAT" | "RTM" | "OTRO";
export type HallazgoRenglon = "DEUDA" | "NO_VIGENTE" | "SIN_VERIFICAR";
export type ResultadoRenglon = "PENDIENTE" | "SANEADO" | "NO_PROCEDE";
export type MotivoNoElegible = "NO_TERMINADO" | "YA_SOLICITADO";
export type MotivoOmision = MotivoNoElegible | "NO_ENCONTRADO";
export type ActorSaneamiento = "COMPANIA" | "FLIT";
export type EventoSaneamiento =
  | "SOLICITADO"
  | "DESMARCADO"
  | "TOMADO"
  | "RENGLON_AGREGADO"
  | "RENGLON_QUITADO"
  | "RENGLON_ACTUALIZADO"
  | "EVIDENCIA_CARGADA"
  | "EVIDENCIA_QUITADA"
  | "OBSERVACION_EDITADA"
  | "CERRADO_SANEADO"
  | "CERRADO_NO_PROCEDE"
  | "REABIERTO"
  | "ANULADO_FLIT";

/** Saneamiento de una fila de la grilla del lote y de la grilla de placas (§6.1 y §6.6). */
export interface SaneamientoFila {
  estado: EstadoSaneamientoActivo | null;
  elegible: boolean;
  motivo_no_elegible: MotivoNoElegible | null;
}

/** Evidencia de un renglón: nunca la referencia del almacén ni el sha (§6.3). */
export interface EvidenciaRenglon {
  nombre: string;
  tamano: number;
  subida_en: string;
}

/** Renglón vigente (SP-2: los quitados no salen en la respuesta). */
export interface RenglonSaneamiento {
  id: string;
  /** xmin del renglón (C6): se devuelve en el PUT del backoffice. */
  version: number;
  fuente: FuenteSaneamiento;
  /** null en OTRO. */
  hallazgo: HallazgoRenglon | null;
  /** Solo en OTRO. */
  descripcion: string | null;
  automatico: boolean;
  resultado: ResultadoRenglon;
  nota: string | null;
  evidencia: EvidenciaRenglon | null;
}

/** Evento del historial que ve la compañía: sin usuario_id; no recibe RENGLON_ACTUALIZADO ni OBSERVACION_EDITADA. */
export interface EventoHistorial {
  evento: EventoSaneamiento;
  ocurrido_en: string;
  actor: ActorSaneamiento;
  fuente: FuenteSaneamiento | null;
  motivo: string | null;
}

/** Saneamiento que ve la compañía (§6.3, SP-15). */
export interface Saneamiento {
  id: string;
  /** xmin del saneamiento (C6). */
  version: number;
  estado: EstadoSaneamiento;
  solicitado_en: string;
  solicitado_por_mi: boolean;
  tomado_en: string | null;
  cerrado_en: string | null;
  motivo_cierre: string | null;
  motivo_anulacion: string | null;
  observacion_flit: string | null;
  renglones: RenglonSaneamiento[];
  historial: EventoHistorial[];
}

export interface OmisionSolicitud {
  vehiculo_id: string;
  motivo: MotivoOmision;
}

export interface ResultadoSolicitud {
  solicitados: number;
  omitidos: OmisionSolicitud[];
}

// ---------- Panel de deuda (§6.5) ----------

export type FuenteDeuda = Exclude<FuenteSaneamiento, "OTRO">;
export type EstadoFuenteDeuda = "CON_DEUDA" | "NO_VIGENTE" | "SIN_VERIFICAR" | "SIN_HALLAZGO" | "NO_APLICA";

interface FuenteDeudaBase {
  estado: EstadoFuenteDeuda;
  /** Fecha y hora de la consulta; ausente o null si no se consultó. */
  consultado_en?: string | null;
  /** Solo con NO_APLICA. */
  motivo_na?: MotivoNa | null;
}

export interface ComparendoDeuda {
  numero: string;
  fecha: string | null;
  /** Texto tal como llega del SIMIT (S17): no se suma ni se convierte. */
  valor: string;
  estado: string | null;
}

/** SIMIT del vehículo, del propietario o del locatario. SP-11: la persona sin nombre. */
export interface FuenteDeudaSimit extends FuenteDeudaBase {
  fuente: "SIMIT_VEHICULO" | "SIMIT_PROPIETARIO" | "SIMIT_LOCATARIO";
  persona?: { tipo_documento: string; documento: string } | null;
  paz_y_salvo?: boolean | null;
  /** Hasta 100, como el detalle de la celda. */
  comparendos?: ComparendoDeuda[];
  comparendos_truncados?: boolean;
  /** UA-6 (BE-C-2): el saldo va como texto, tal como llega del SIMIT; solo se formatea si es numérico. */
  acuerdos_pago?: Array<{ saldo_pendiente: string }>;
}

export interface FuenteDeudaImpuesto extends FuenteDeudaBase {
  fuente: "IMPUESTO";
  monto_total?: number | null;
  vigencias?: Array<{ anio: number; valor: number; pagada: boolean }>;
}

export interface FuenteDeudaVigencia extends FuenteDeudaBase {
  fuente: "SOAT" | "RTM";
  vence?: string | null;
}

export type FuenteDeudaPanel = FuenteDeudaSimit | FuenteDeudaImpuesto | FuenteDeudaVigencia;

export interface MotivoDictamenDeuda {
  codigo: string;
  efecto: "BLOQUEA" | "ADVIERTE";
  texto: string;
}

export interface PanelDeuda {
  vehiculo: { id: string; placa: string | null };
  modo: ModoDiagnostico;
  /** null en modo Simple o sin dictamen vigente. */
  dictamen: { veredicto: "VIABLE" | "BLOQUEADO"; catalogo_version: number; motivos: MotivoDictamenDeuda[] } | null;
  fuentes: FuenteDeudaPanel[];
  saneamiento: { estado: EstadoSaneamientoActivo } | null;
}

// ---------- Llamadas ----------

/** Tope del masivo (§6.1, D-11). */
export const MAXIMO_SOLICITUD_MASIVA = 500;
/** Tope del Excel de varias placas (§6.1, D-11). */
export const MAXIMO_DEUDA_VARIAS = 200;

const seg = encodeURIComponent;
const rutaVehiculo = (id: string, vid: string) => `/diagnosticos/${seg(id)}/vehiculos/${seg(vid)}`;
const rutaSaneamiento = (id: string, vid: string) => `${rutaVehiculo(id, vid)}/saneamiento`;

const inesperado = () => new Error("Diagnóstico respondió algo inesperado. Intenta de nuevo en un momento.");

/** Ids sin repetir y dentro del tope; si no, el mismo 400 LISTA_INVALIDA del servidor, sin salir a la red. */
function listaDeIds(ids: readonly string[], maximo: number): string[] {
  const unicos = [...new Set(ids)];
  if (unicos.length === 0 || unicos.length > maximo) throw new ApiError(400, `Elige entre 1 y ${maximo} vehículos.`, "LISTA_INVALIDA");
  return unicos;
}

/** PUT …/saneamiento: marca. 201, o 200 si ya estaba SOLICITADO (idempotente). 409 VEHICULO_NO_TERMINADO / YA_SOLICITADO. */
export function marcarSaneamiento(id: string, vid: string): Promise<Saneamiento> {
  return apiFetch<Saneamiento>(rutaSaneamiento(id, vid), { method: "PUT" });
}

/** DELETE …/saneamiento: desmarca. 404 SIN_SANEAMIENTO cuenta como éxito (ya no estaba). 409 SANEAMIENTO_EN_GESTION lanza. */
export async function desmarcarSaneamiento(id: string, vid: string): Promise<void> {
  try {
    await apiFetch<void>(rutaSaneamiento(id, vid), { method: "DELETE" });
  } catch (error) {
    if (error instanceof ApiError && error.status === 404 && error.code === "SIN_SANEAMIENTO") return;
    throw error;
  }
}

/** POST /saneamientos/solicitudes: masivo síncrono de 1 a 500 vehículos de uno o varios lotes. */
export async function solicitarSaneamientos(vehiculoIds: readonly string[]): Promise<ResultadoSolicitud> {
  const vehiculo_ids = listaDeIds(vehiculoIds, MAXIMO_SOLICITUD_MASIVA);
  const r = await apiFetch<ResultadoSolicitud | null>("/saneamientos/solicitudes", { method: "POST", body: { vehiculo_ids } });
  if (!r || typeof r.solicitados !== "number" || !Array.isArray(r.omitidos)) throw inesperado();
  return r;
}

/** GET …/saneamiento: detalle; null si el vehículo no tiene saneamiento (404 SIN_SANEAMIENTO). */
export async function obtenerSaneamiento(id: string, vid: string, signal?: AbortSignal): Promise<Saneamiento | null> {
  try {
    return await apiFetch<Saneamiento>(rutaSaneamiento(id, vid), { signal, cache: "no-store" });
  } catch (error) {
    if (error instanceof ApiError && error.status === 404 && error.code === "SIN_SANEAMIENTO") return null;
    throw error;
  }
}

/** URL del BFF de la evidencia de un renglón (para `useDescarga`). */
export const urlEvidencia = (id: string, vid: string, rid: string): string =>
  `${API_PREFIX}${rutaSaneamiento(id, vid)}/renglones/${seg(rid)}/evidencia`;

/** GET …/deuda: panel de deuda (§6.5). 409 VEHICULO_NO_TERMINADO si aún hay consultas en curso. */
export function obtenerDeuda(id: string, vid: string, signal?: AbortSignal): Promise<PanelDeuda> {
  return apiFetch<PanelDeuda>(`${rutaVehiculo(id, vid)}/deuda`, { signal, cache: "no-store" });
}

export const urlDeudaPdf = (id: string, vid: string): string => `${API_PREFIX}${rutaVehiculo(id, vid)}/deuda/pdf`;
export const urlDeudaExcel = (id: string, vid: string): string => `${API_PREFIX}${rutaVehiculo(id, vid)}/deuda/excel`;

/**
 * POST /vehiculos/deuda/excel: Excel de 1 a 200 placas. Es un POST con cuerpo, así que no sirve el enlace con `download`:
 * se baja por blob con el nombre del Content-Disposition. Un error HTTP lanza ApiError con su `code` y no descarga nada.
 */
export async function descargarExcelDeudaVarias(vehiculoIds: readonly string[]): Promise<string> {
  const vehiculo_ids = listaDeIds(vehiculoIds, MAXIMO_DEUDA_VARIAS);
  if (isReauthenticating()) return new Promise<string>(() => {});
  const respuesta = await fetch(`${API_PREFIX}/vehiculos/deuda/excel`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ vehiculo_ids }),
    cache: "no-store",
  });
  if (!respuesta.ok) {
    if (respuesta.status === 401) reauthenticate({ silent: true });
    const datos = await leerError(respuesta);
    throw new ApiError(respuesta.status, datos?.error ?? "No se pudo descargar el archivo.", datos?.code);
  }
  const nombre = nombreArchivo(respuesta.headers.get("Content-Disposition"), "deuda-placas.xlsx");
  const url = URL.createObjectURL(await respuesta.blob());
  const a = document.createElement("a");
  a.href = url;
  a.download = nombre;
  a.rel = "noopener";
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 0);
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

// ---------- Errores ----------

/** Textos fijos por `code` (spec-ui §1.3, §3.2, §5.1, §6.3, §6.5): nunca el código ni lo digitado. */
const MENSAJES: Record<string, string> = {
  VEHICULO_NO_TERMINADO: "Este vehículo aún tiene consultas por terminar.",
  YA_SOLICITADO: "Este vehículo ya tiene un saneamiento.",
  SANEAMIENTO_EN_GESTION: "FLIT ya empezó este saneamiento; ya no se puede quitar.",
  LISTA_INVALIDA: "No pudimos solicitar el saneamiento. Intenta de nuevo.",
  VERSION_DESACTUALIZADA: "Otra persona cambió este saneamiento.",
  NO_EDITABLE: "El saneamiento ya no está en gestión.",
  TRANSICION_INVALIDA: "El estado cambió mientras tanto. Recarga para ver el estado actual.",
  ARCHIVO_MUY_GRANDE: "El archivo supera 10 MB.",
  ARCHIVO_NO_ES_PDF: "El archivo no es un PDF válido.",
  ARCHIVO_REQUERIDO: "Elige un archivo PDF.",
  MOTIVO_REQUERIDO: "Escribe al menos 10 caracteres.",
  DEMASIADAS_FILAS: "Hay más de 5.000 saneamientos con estos filtros. Acota las fechas o el estado para descargar.",
};

const GENERICO = "No pudimos guardar el cambio. Intenta de nuevo.";

/**
 * Texto para el usuario de un error del saneamiento: el de su `code` si se conoce; si no, `respaldo` (el texto de la
 * pantalla, p. ej. «No pudimos solicitar el saneamiento…») o el genérico. Un 403 se trata aparte (la pantalla va a /403).
 */
export function mensajeErrorSaneamiento(error: unknown, respaldo: string = GENERICO): string {
  if (error instanceof ApiError && error.code && Object.prototype.hasOwnProperty.call(MENSAJES, error.code)) return MENSAJES[error.code];
  return respaldo;
}
