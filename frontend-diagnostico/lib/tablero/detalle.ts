// Detalle de una celda del tablero (HU #13801, F06-H4). Tipos del contrato de HU #13798 (operationId obtenerDetalleCelda
// en contracts/openapi/diagnostico.v1.yaml): GET /diagnosticos/{id}/vehiculos/{vid}/consultas/{fuente}, en snake_case.
// El DTO es cerrado: nunca trae origen, usuario manual, proveedor, transacción, nombres ni documentos completos (R-20), así
// que la vista de la compañía no tiene con qué distinguir una celda llenada a mano (Q5).
import { ApiError, apiFetch } from "@/lib/api";
import type { Fuente } from "@/lib/configuracion";
import type { EstadoCelda, MotivoNa } from "@/lib/tablero/api";

export interface PropietarioRunt {
  tipo_documento: string | null;
  tipo_propiedad: string | null;
}

export interface ResultadoRuntDetalle {
  placa: string | null;
  vin: string | null;
  servicio: string | null;
  clase: string | null;
  organismo: string | null;
  propietarios: PropietarioRunt[];
  locatarios: number;
}

export interface Comparendo {
  numero: string | null;
  fecha: string | null;
  /** Texto decimal en pesos. */
  valor: string | null;
  estado: string | null;
  organismo: string | null;
}

export interface ResultadoSimitDetalle {
  semaforo: "VERDE" | "AMARILLO" | "ROJO" | null;
  /** Total de comparendos, aunque la lista venga cortada. */
  cantidad_comparendos: number;
  /** Como mucho 100. */
  comparendos: Comparendo[];
  comparendos_truncados: boolean;
}

export interface FuenteAvaluo {
  fuente: string | null;
  estado: "OK" | "SIN_DATOS" | "ERROR" | null;
  valor: number | null;
  muestras: number | null;
  fecha: string | null;
}

export interface ResultadoAvaluoDetalle {
  sin_valor_comercial: boolean;
  valor_sugerido: number | null;
  fuente_principal: "fasecolda" | "mercado_libre" | "base_gravable" | null;
  fuentes: FuenteAvaluo[];
}

export interface ResultadoRuntPersona {
  inscrito: boolean;
  estado_conductor: string | null;
  licencias: { categoria: string | null; estado: string | null; vigente_hasta: string | null }[];
}

export interface ResultadoRues {
  encontrada: boolean;
  estado_matricula: string | null;
  ultima_renovacion: string | null;
}

export type ConsultaPersona = "RUNT_PERSONA" | "RUES" | "SIMIT";
export type EstadoPersona = "OK" | "ERROR" | "SIN_RESULTADO";

export interface PersonaDetalle {
  orden: number;
  rol: "PROPIETARIO" | "LOCATARIO";
  tipo_documento: string;
  /** «****» más los cuatro últimos caracteres; se muestra tal cual llega, nunca se reconstruye. */
  documento_enmascarado: string;
  declarado_por_cliente: boolean;
  consulta: ConsultaPersona | null;
  estado: EstadoPersona;
  tipo_error: string | null;
  consultado_en: string | null;
  resultado: ResultadoRuntPersona | ResultadoRues | ResultadoSimitDetalle | null;
}

export interface DetalleCelda {
  id: string;
  vehiculo_id: string;
  fuente: Fuente;
  estado: EstadoCelda;
  motivo_na: MotivoNa | null;
  tipo_error: string | null;
  dato_obtenido_en: string | null;
  reutilizada: boolean;
  /** Solo RUNT, SIMIT_VEHICULO y AVALUO con la celda COMPLETADO. */
  resultado: ResultadoRuntDetalle | ResultadoSimitDetalle | ResultadoAvaluoDetalle | null;
  /** Solo en las fuentes de persona; una entrada por persona del rol, en orden. */
  personas: PersonaDetalle[] | null;
}

/** Carga el detalle de una celda; la pantalla de backoffice (F06-H5) pasa la suya con `?empresa=`. */
export type CargadorDetalle = (loteId: string, vehiculoId: string, fuente: Fuente, signal?: AbortSignal) => Promise<DetalleCelda>;

export type TipoErrorDetalle = "NO_DISPONIBLE" | "SIN_PERMISO" | "FALLO";

export interface ErrorDetalle {
  tipo: TipoErrorDetalle;
  titulo: string;
  mensaje: string;
}

/** Ruta del detalle bajo /api/v1/diagnostico. */
export function rutaDetalleCelda(loteId: string, vehiculoId: string, fuente: Fuente): string {
  return `/diagnosticos/${encodeURIComponent(loteId)}/vehiculos/${encodeURIComponent(vehiculoId)}/consultas/${encodeURIComponent(fuente)}`;
}

/** GET /diagnosticos/{id}/vehiculos/{vid}/consultas/{fuente}. */
export const obtenerDetalleCelda: CargadorDetalle = (loteId, vehiculoId, fuente, signal) =>
  apiFetch<DetalleCelda>(rutaDetalleCelda(loteId, vehiculoId, fuente), { method: "GET", signal });

/** 404 → no disponible (otra compañía, otro lote o fuente sin celda); 403 → sin permiso; el resto, el mensaje de la API. */
export function traducirErrorDetalle(error: unknown): ErrorDetalle {
  if (error instanceof ApiError && error.status === 404)
    return { tipo: "NO_DISPONIBLE", titulo: "Este detalle no está disponible", mensaje: "La consulta no existe o no tienes acceso a ella." };
  if (error instanceof ApiError && error.status === 403)
    return { tipo: "SIN_PERMISO", titulo: "No tienes permiso para ver este detalle", mensaje: "Pídele acceso al administrador de tu compañía." };
  return { tipo: "FALLO", titulo: "No se pudo cargar el detalle", mensaje: error instanceof Error ? error.message : "Intenta de nuevo en un momento." };
}

const TIPOS_ERROR: Record<string, string> = {
  TIMEOUT: "La fuente no respondió a tiempo.",
  RED: "No hubo conexión con la fuente.",
  HTTP_5XX: "La fuente no estaba disponible.",
  FUENTE_NO_DISPONIBLE: "La fuente no estaba disponible.",
  RESPUESTA_INVALIDA: "La fuente respondió con datos que no se pudieron leer.",
  ESTADO_NO_OK: "La fuente rechazó la consulta.",
  NO_CONECTADO: "Esta fuente no está conectada en este ambiente.",
  PERSONAS_CON_ERROR: "Algunas personas no se pudieron consultar; lo que sí respondió se muestra abajo.",
  BLOQUEADA_POR_RUNT: "Depende del RUNT del vehículo, que también falló. Se resuelve al reprocesar el RUNT.",
  SIN_PLACA: "El vehículo no tiene placa según el RUNT: no se puede consultar.",
  SIN_IDENTIFICADOR: "El vehículo no tiene ni placa ni VIN según el RUNT: no se puede consultar.",
};

/** Texto claro del tipo_error de la celda o de una persona; un código desconocido no se muestra crudo. */
export function textoTipoError(codigo: string | null): string {
  return (codigo && TIPOS_ERROR[codigo]) || "La fuente respondió con un error.";
}

const MOTIVOS_NA: Record<MotivoNa, string> = {
  SERVICIO_PUBLICO: "El vehículo es de servicio público: esta consulta no aplica.",
  DEPARTAMENTO_NO_SOPORTADO: "El departamento del vehículo todavía no está soportado.",
  SIN_DOCUMENTO_LOCATARIO: "El vehículo está sin el documento del locatario: no hay a quién consultar.",
};

/** Texto del motivo de una N/A. */
export function textoMotivoNa(motivo: MotivoNa | null): string {
  return (motivo && MOTIVOS_NA[motivo]) || "Esta consulta no aplica a este vehículo.";
}

const PESOS = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0, minimumFractionDigits: 0 });

/** Pesos colombianos sin decimales: «$ 500.000»; sin valor, «—». */
export function formatearPesos(valor: string | number | null): string {
  if (valor === null || valor === "") return "—";
  const numero = typeof valor === "number" ? valor : Number(valor);
  return Number.isFinite(numero) ? PESOS.format(numero) : "—";
}

/** «AAAA-MM-DD» → «DD/MM/AAAA»; cualquier otra forma, tal cual. */
export function fechaCorta(fecha: string | null): string {
  if (!fecha) return "—";
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(fecha);
  return m ? `${m[3]}/${m[2]}/${m[1]}` : fecha;
}

/** Fuentes cuyo detalle es una sección por persona. */
export const FUENTES_PERSONA: ReadonlySet<Fuente> = new Set<Fuente>(["RUNT_PROPIETARIO", "RUNT_LOCATARIO", "SIMIT_PROPIETARIO", "SIMIT_LOCATARIO"]);
