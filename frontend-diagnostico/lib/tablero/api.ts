// Tablero del lote (HU #13748, F04-H2). Tipos y llamadas del contrato de HU #13743 (GET /diagnosticos/{id}/grilla?desde=,
// POST /diagnosticos/{id}/cerrar-panel) y del reproceso de HU #13746 (POST /consultas/{cid}/reprocesar), en snake_case.
// La grilla nunca trae el origen de la celda ni quién la llenó (R-20): la pantalla no muestra ningún indicador de dato
// manual. La API vuelve a validar cada permiso.
import { ApiError, apiFetch } from "@/lib/api";
import type { Fuente } from "@/lib/configuracion";
import type { EstadoDiagnostico } from "@/lib/diagnosticos/api";

export type EstadoCelda = "PENDIENTE" | "EN_CURSO" | "COMPLETADO" | "ERROR" | "NA";
export type MotivoNa = "SERVICIO_PUBLICO" | "DEPARTAMENTO_NO_SOPORTADO" | "SIN_DOCUMENTO_LOCATARIO";

/** tipo_error de la celda que depende de un RUNT en error: no se reprocesa sola (409), se reprocesa el RUNT. */
export const BLOQUEADA_POR_RUNT = "BLOQUEADA_POR_RUNT";

export interface CeldaGrilla {
  id: string;
  fuente: Fuente;
  estado: EstadoCelda;
  motivo_na: MotivoNa | null;
  tipo_error: string | null;
  /** Fecha y hora del dato; en la celda reutilizada, la del dato original (D-10). */
  dato_obtenido_en: string | null;
  reutilizada: boolean;
  version: number;
}

export interface FilaVehiculo {
  id: string;
  orden: number;
  identificador: string;
  tipo_identificador: string;
  placa: string | null;
  vin: string | null;
  /** Todas sus celdas en COMPLETADO o NA (R-7), calculado con todas, no solo con las recibidas. */
  terminado: boolean;
  celdas: CeldaGrilla[];
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
  version: number;
  desde: number;
  columnas: ColumnasGrilla;
  avance: { vehiculos: number; terminados: number };
  vehiculos: FilaVehiculo[];
  /** Solo con estimado AL_FINALIZAR y el lote COMPLETADO o INCOMPLETO. */
  estimado_consultas?: EstimadoVsConsumido[];
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
