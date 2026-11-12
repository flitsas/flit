// Tablero del lote (HU #13748, F04-H2). Tipos y llamadas del contrato de HU #13743 (GET /diagnosticos/{id}/grilla?desde=,
// POST /diagnosticos/{id}/cerrar-panel) y del reproceso de HU #13746 (POST /consultas/{cid}/reprocesar), en snake_case.
// La grilla nunca trae el origen de la celda ni quién la llenó (R-20): la pantalla no muestra ningún indicador de dato
// manual. La API vuelve a validar cada permiso.
import { ApiError, apiFetch } from "@/lib/api";
import type { Fuente } from "@/lib/configuracion";
import type { EstadoDiagnostico, ModoDiagnostico } from "@/lib/diagnosticos/api";

export type EstadoCelda = "PENDIENTE" | "EN_CURSO" | "COMPLETADO" | "ERROR" | "NA";
export type MotivoNa = "SERVICIO_PUBLICO" | "DEPARTAMENTO_NO_SOPORTADO" | "SIN_DOCUMENTO_LOCATARIO";

/** tipo_error de la celda que depende de un RUNT en error: no se reprocesa sola (409), se reprocesa el RUNT. */
export const BLOQUEADA_POR_RUNT = "BLOQUEADA_POR_RUNT";

export interface CeldaGrilla {
  id: string;
  fuente: Fuente;
  estado: EstadoCelda;
  /** Punto 5: nombre de negocio del estado (opcional). El chip del semáforo conserva su texto homologado (HU #14512). */
  estado_etiqueta?: string | null;
  motivo_na: MotivoNa | null;
  /** Punto 5: nombre de negocio del motivo de la N/A (opcional; sin él, el diccionario local). */
  motivo_na_etiqueta?: string | null;
  tipo_error: string | null;
  /** Fecha y hora del dato; en la celda reutilizada, la del dato original (D-10). */
  dato_obtenido_en: string | null;
  reutilizada: boolean;
  version: number;
  /** Impuesto COMPLETADO con deuda confirmada. El semáforo no se pinta en verde. */
  tiene_deuda?: boolean;
}

/** Documento de persona que declaró el cliente en el lote (HU #14087); llega enmascarado («****» y los 4 últimos). */
export interface DocumentoDeclaradoFila {
  rol: "PROPIETARIO" | "LOCATARIO";
  tipo_documento: string;
  documento_enmascarado: string;
}

/** Documento de un propietario del vehículo según el RUNT (HU #14087); llega enmascarado. */
export interface DocumentoPropietarioFila {
  tipo_documento: string;
  documento_enmascarado: string;
}

export interface FilaVehiculo {
  id: string;
  orden: number;
  identificador: string;
  tipo_identificador: string;
  /** Punto 5: nombre de negocio del tipo de identificador (opcional; sin él, `TIPOS_IDENTIFICADOR` de lib/rotulos). */
  tipo_identificador_etiqueta?: string | null;
  placa: string | null;
  vin: string | null;
  /** Todas sus celdas en COMPLETADO o NA (R-7), calculado con todas, no solo con las recibidas. */
  terminado: boolean;
  /**
   * HU #14087: documento declarado por el cliente (null si no declaró ninguno). Opcional en el tipo porque la fila solo viaja
   * en el sondeo cuando cambia una celda y la grilla del backoffice puede no traerlo: sin la clave, la pantalla pinta «—».
   */
  documento_declarado?: DocumentoDeclaradoFila | null;
  /** HU #14087: documentos de los propietarios del vehículo, en orden; vacío si aún no se conocen. */
  propietarios?: DocumentoPropietarioFila[];
  /**
   * Punto 3 (ola 2026-11-11): alguna celda en ERROR o en PENDIENTE/EN_CURSO sin respuesta hace más de 10 min. Solo entonces
   * hay «Reintentar placa». Sin la clave (backend previo o fila sin cambios), se trata como false.
   */
  pegada?: boolean;
  /**
   * Punto 4 (incremento 2, contrato provisional; ver lib/tablero/derivacion.ts): el borrador en Trámites que originó la placa,
   * si existe. Sin la clave (backend previo), no hay acción de trámite.
   */
  tramite?: TramiteFila | null;
  /** Punto 4: la placa se puede derivar (VIABLE, terminada y sin trámite). */
  elegible_tramite?: boolean;
  /** Punto 4: código o texto del motivo por el que no es elegible (NO_VIABLE, NO_TERMINADO, YA_TIENE_TRAMITE, EN_CURSO…). */
  motivo_no_elegible?: string | null;
  /**
   * Punto 4 (contrato del incremento 2): derivación de la fila; null con la derivación apagada. Manda sobre las claves planas
   * de arriba (que se conservan por compatibilidad). Se lee con `derivacionDeFila` (lib/tablero/derivacion.ts).
   */
  derivacion?: {
    elegible: boolean;
    motivo_no_elegible: string | null;
    estado: "PENDIENTE" | "ENVIADO" | "CREADO" | "OMITIDO" | "FALLIDO" | null;
    tramite: TramiteFila | null;
  } | null;
  celdas: CeldaGrilla[];
}

/** Trámite creado desde el diagnóstico (Punto 4): id del borrador en Trámites y su número visible. */
export interface TramiteFila {
  id: string;
  numero: string | number;
  /** Enlace al borrador que arma el backend ({UrlTramites}/tramites/{id}?t={empresa}); puede ser relativo o faltar. */
  url?: string;
}

export interface ColumnasGrilla {
  por_vehiculo: Fuente[];
  por_propietario: Fuente[];
  por_locatario: Fuente[];
}

export interface EstimadoVsConsumido {
  fuente: Fuente;
  estimadas: number;
  consumidas: number;
  /**
   * HU #13796: las estimadas son un mínimo (fuente por persona y algún vehículo sin personas registradas). Sin la propiedad,
   * el número es exacto.
   */
  minimo?: boolean;
}

export interface Grilla {
  id: string;
  nombre: string;
  estado: EstadoDiagnostico;
  /** Punto 5: nombre de negocio del estado del lote (opcional; sin él, `ESTADOS_LOTE`). */
  estado_etiqueta?: string | null;
  version: number;
  desde: number;
  columnas: ColumnasGrilla;
  avance: { vehiculos: number; terminados: number };
  vehiculos: FilaVehiculo[];
  /** Solo con estimado AL_FINALIZAR y el lote COMPLETADO o INCOMPLETO. */
  estimado_consultas?: EstimadoVsConsumido[];
  /** Punto 3: placas pegadas del lote; viaja en cada respuesta, como el avance. Sin la clave, 0. */
  total_pegadas?: number;
  /** Punto 3: ids de las placas pegadas, en cada respuesta. Cubre las que se pegan solo por tiempo (> 10 min sin cambio de celda),
   * que el sondeo incremental no reenvía. Sin la clave, se conserva el `pegada` de cada fila. */
  vehiculos_pegados?: string[];
  /** Punto 4 (provisional): modo del lote; en SIMPLE, «Crear trámite» pide el tipo. Sin la clave, se pregunta solo si la API lo exige. */
  modo?: ModoDiagnostico;
}

export interface PanelCerrado {
  id: string;
  estado: "INCOMPLETO";
  cerrado_en: string;
}

const ruta = (id: string, sufijo: string) => `/diagnosticos/${encodeURIComponent(id)}${sufijo}`;

/** GET /diagnosticos/{id}/grilla?desde=N: solo vehículos y celdas con versión mayor que N; estado, columnas y avance siempre. */
export function obtenerGrilla(id: string, desde: number, signal?: AbortSignal): Promise<Grilla> {
  return apiFetch<Grilla>(ruta(id, `/grilla?desde=${desde}`), { signal });
}

/** POST /diagnosticos/{id}/cerrar-panel: el lote terminado con errores pasa a INCOMPLETO. */
export function cerrarPanel(id: string): Promise<PanelCerrado> {
  return apiFetch<PanelCerrado>(ruta(id, "/cerrar-panel"), { method: "POST" });
}

/** POST /consultas/{cid}/reprocesar (HU #13746): la celda en ERROR vuelve a PENDIENTE y el lote a EN_PROCESO. */
export async function reprocesarConsulta(consultaId: string): Promise<void> {
  await apiFetch<unknown>(`/consultas/${encodeURIComponent(consultaId)}/reprocesar`, { method: "POST" });
}

/** Respuesta de los dos reintentos manuales (Punto 3): cuántas celdas volvieron a PENDIENTE. */
export interface ResultadoReintento {
  reencoladas: number;
}

/** Rutas de los reintentos manuales, aisladas aquí por si el backend cambia el nombre. */
export const rutaReintentarVehiculo = (id: string, vehiculoId: string) => ruta(id, `/vehiculos/${encodeURIComponent(vehiculoId)}/reintentar`);
export const rutaReintentarPegadas = (id: string) => ruta(id, "/reintentar-pegadas");

/** POST /diagnosticos/{id}/vehiculos/{vid}/reintentar: solo las celdas pegadas de la placa vuelven a PENDIENTE. */
export function reintentarVehiculo(id: string, vehiculoId: string): Promise<ResultadoReintento> {
  return apiFetch<ResultadoReintento>(rutaReintentarVehiculo(id, vehiculoId), { method: "POST" });
}

/** POST /diagnosticos/{id}/reintentar-pegadas: todas las celdas pegadas del lote vuelven a PENDIENTE. */
export function reintentarPegadas(id: string): Promise<ResultadoReintento> {
  return apiFetch<ResultadoReintento>(rutaReintentarPegadas(id), { method: "POST" });
}

/** «N consultas reenviadas» (singular con 1). */
export function mensajeReenviadas(reencoladas: number): string {
  const n = Number.isFinite(reencoladas) ? Math.max(0, Math.trunc(reencoladas)) : 0;
  if (n === 0) return "No había consultas pegadas para reenviar.";
  return n === 1 ? "1 consulta reenviada" : `${n} consultas reenviadas`;
}

/** Mensaje claro de un error de los reintentos manuales; cualquier otro, el de la API. */
export function mensajeReintento(error: unknown): string {
  if (error instanceof ApiError && error.status === 403) return "No tienes permiso para reintentar consultas.";
  return error instanceof Error && error.message ? error.message : "No se pudieron reintentar las consultas.";
}

const MENSAJES_CIERRE: Record<string, string> = {
  LOTE_NO_EN_PROCESO: "El lote ya no está en proceso: no hay panel que cerrar.",
  CELDAS_SIN_TERMINAR: "Todavía hay consultas pendientes o en curso. Espera a que terminen para cerrar el panel.",
  LOTE_SIN_ERRORES: "El lote no tiene consultas con error: se completa solo, sin cerrar el panel.",
  LOTE_MODIFICADO: "El lote cambió mientras cerrabas el panel. Ya ves su estado actual; vuelve a intentarlo si aún aplica.",
};

/** Mensaje claro de cada 409 de POST /cerrar-panel; cualquier otro error, el mensaje de la API. */
export function mensajeCierre(error: unknown): string {
  if (error instanceof ApiError && error.status === 409 && error.code && MENSAJES_CIERRE[error.code]) return MENSAJES_CIERRE[error.code];
  return error instanceof Error ? error.message : "No se pudo cerrar el panel.";
}

/** Mensaje claro de cada error de POST /consultas/{cid}/reprocesar. */
export function mensajeReproceso(error: unknown): string {
  if (error instanceof ApiError && error.status === 403) return "No tienes permiso para reprocesar consultas.";
  if (error instanceof ApiError && error.status === 409)
    return "Esta consulta ya no se puede reprocesar: ya no está en error o depende del RUNT del vehículo, que también falló. Reprocesa primero el RUNT.";
  return error instanceof Error ? error.message : "No se pudo reprocesar la consulta.";
}
