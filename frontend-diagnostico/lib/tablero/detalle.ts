// Detalle de una celda del tablero (HU #13801, F06-H4). Tipos del contrato de HU #13798 (operationId obtenerDetalleCelda
// en contracts/openapi/diagnostico.v1.yaml): GET /diagnosticos/{id}/vehiculos/{vid}/consultas/{fuente}, en snake_case.
// El DTO es cerrado fuera de `resultado` y `respuesta_fuente`: nunca trae origen ni usuario manual (R-20), así que la vista de
// la compañía no tiene con qué distinguir una celda llenada a mano (Q5). `respuesta_fuente` (HU #14087 AC8 ajustado) trae TODO
// lo que respondió la fuente, incluidos datos personales, por decisión del usuario; la página del detalle solo pinta los datos
// de la consulta, en español (lib/tablero/etiquetasRespuesta). El detalle trae además `identificador` y `nombre_diagnostico`
// para que la página en pestaña nueva se titule sola (HU #14087).
import { ApiError, apiFetch } from "@/lib/api";
import { FUENTES, type Fuente } from "@/lib/configuracion";
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
  /** HU #14563 B4: los campos tipados se omiten (o llegan null) si la fuente no los informó; nunca se infieren. */
  total_general?: string | null;
  total_por_pagar?: string | null;
  paz_y_salvo?: boolean | null;
  suspendida?: boolean | null;
  cancelada?: boolean | null;
  multas?: MultaSimit[] | null;
  acuerdos_pago?: AcuerdoPagoSimit[] | null;
  resoluciones?: ResolucionSimit[] | null;
  cantidad_resoluciones?: number | null;
}

/** HU #14563 B4. Infracción de una multa; `descripcion` null si la fuente no la trae. */
export interface InfraccionSimit {
  codigo?: string | null;
  descripcion?: string | null;
}

/** HU #14563 B4. Multa del SIMIT, sin el infractor. */
export interface MultaSimit {
  numero?: string | null;
  fecha?: string | null;
  valor?: string | null;
  valor_pagar?: string | null;
  estado_cartera?: string | null;
  organismo?: string | null;
  codigos?: string | null;
  infracciones?: InfraccionSimit[] | null;
}

/** HU #14563 B4. Acuerdo de pago del SIMIT. */
export interface AcuerdoPagoSimit {
  estado?: string | null;
  cuotas_abonadas?: string | null;
  cuotas_pendientes?: string | null;
  pendiente?: string | null;
  fecha?: string | null;
  organismo?: string | null;
}

/** HU #14563 B4. Resolución del SIMIT. */
export interface ResolucionSimit {
  numero?: string | null;
  fecha?: string | null;
  estado?: string | null;
  valor?: string | null;
  organismo?: string | null;
}

/** HU #14563 B5. Un intento no manual de la consulta. `tipo_error` (D15) es opcional: etiqueta de negocio. */
export interface IntentoConsultaCliente {
  n: number;
  inicio: string;
  fin: string;
  duracion_ms: number;
  resultado: "EXITOSO" | "ERROR";
  disparador: "AUTOMATICO" | "REPROCESO";
  desde_cache: boolean;
  tipo_error?: string | null;
}

/** HU #14563 B5. Auditoría de la consulta visible a la compañía. */
export interface AuditoriaConsulta {
  inicio: string;
  fin: string;
  duracion_ms: number;
  intentos: number;
  intentos_detalle: IntentoConsultaCliente[];
}

export const FRASE_SIN_PAZ_Y_SALVO_INFORMADO = "La fuente no informó paz y salvo";
export const FRASE_SIN_VALOR_INFORMADO = "Sin valor informado";
export const FRASE_SIN_RESOLUCIONES_INFORMADAS = "La fuente no informó resoluciones";

/** Texto del paz y salvo: solo lo que afirma la fuente; nunca se deduce de cero comparendos (R4). */
export function textoPazYSalvo(valor: boolean | null | undefined): string {
  if (valor === true) return "Está a paz y salvo";
  if (valor === false) return "No está a paz y salvo";
  return FRASE_SIN_PAZ_Y_SALVO_INFORMADO;
}

/** Total adeudado: `total_por_pagar` y, si falta, `total_general`. Ausente → «Sin valor informado», nunca «$ 0». */
export function textoTotalSimit(r: ResultadoSimitDetalle): string {
  const valor = r.total_por_pagar ?? r.total_general ?? null;
  return valor === null || valor === "" ? FRASE_SIN_VALOR_INFORMADO : formatearPesos(valor);
}

/** Infracción «código · descripción»; sin descripción, solo el código. */
export function textoInfraccion(i: InfraccionSimit): string {
  const codigo = i.codigo ?? "";
  return i.descripcion ? (codigo ? `${codigo} · ${i.descripcion}` : i.descripcion) : codigo || "—";
}

/** Duración legible de la auditoría: «850 ms» o «4,2 s». */
export function textoDuracion(ms: number): string {
  if (ms < 1000) return `${ms} ms`;
  return `${(ms / 1000).toLocaleString("es-CO", { maximumFractionDigits: 1 })} s`;
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

/** Vigencia del impuesto departamental (HU #13864, Q1 = a): año, valor en pesos enteros y si está pagada. */
export interface VigenciaImpuestoDetalle {
  anio: number;
  valor: number;
  pagada: boolean;
}

/** Último pago informado. Un campo que no vino no se pinta. */
export interface UltimoPagoImpuesto {
  anio?: number | null;
  valor?: number | null;
  fecha?: string | null;
}

/**
 * Impuesto vehicular. `tiene_deuda` confirma la deuda. `monto_total` falta cuando no se informó valor (tampoco es 0).
 * Los opcionales solo vienen si la fuente los trajo.
 */
export interface ResultadoImpuestoDetalle {
  tiene_deuda?: boolean;
  monto_total?: number | null;
  vigencias: VigenciaImpuestoDetalle[];
  intereses_mora?: number | null;
  sanciones?: number | null;
  avaluo_comercial?: number | null;
  ultimo_pago?: UltimoPagoImpuesto | null;
  liquidacion_consolidada_id?: string | null;
  periodo_certificacion?: string | null;
  fecha_consulta?: string | null;
}

export const FRASE_DEUDA_SIN_VALOR = "Deuda sin valor informado";
export const FRASE_SIN_DEUDA = "Sin deuda";
export const FRASE_DATO_PARCIAL = "Dato parcial. El monto que trajo la fuente no se confirma y no se muestra.";

/** Frase del impuesto cuando no hay un monto que afirmar. Null si el total informado se muestra tal cual. */
export function fraseImpuesto(resultado: ResultadoImpuestoDetalle): string | null {
  const sinMonto = resultado.monto_total === null || resultado.monto_total === undefined || resultado.monto_total === 0;
  if (resultado.tiene_deuda && sinMonto) return FRASE_DEUDA_SIN_VALOR;
  if (resultado.tiene_deuda === false && sinMonto) return FRASE_SIN_DEUDA;
  return null;
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
  /** «****» más los cuatro últimos caracteres. La grilla usa este valor. */
  documento_enmascarado: string;
  /** Número completo en el detalle de la consulta. Si falta, se muestra el enmascarado. */
  documento?: string | null;
  declarado_por_cliente: boolean;
  consulta: ConsultaPersona | null;
  estado: EstadoPersona;
  tipo_error: string | null;
  consultado_en: string | null;
  resultado: ResultadoRuntPersona | ResultadoRues | ResultadoSimitDetalle | null;
  /** Respuesta completa de la fuente para esta persona (HU #14087 AC8); ver `DetalleCelda.respuesta_fuente`. */
  respuesta_fuente?: RespuestaFuenteCompleta | null;
  /**
   * Respuesta íntegra del proveedor para esta persona (HU #14154 RUNT persona y RUES; HU #14184 SIMIT propietario y
   * locatario): el JSON del proveedor ya parseado, sin quitar nada (ids internos, firma y campos técnicos incluidos).
   * Ausente o null en consultas anteriores al cambio, servidas desde caché o llenadas a mano. La pintan
   * FUENTES_RESPUESTA_PROVEEDOR.
   */
  respuesta_proveedor?: RespuestaProveedor | null;
}

/** JSON del proveedor tal cual: normalmente un objeto, pero se trata como valor JSON arbitrario por robustez. */
export type RespuestaProveedor = unknown;

/**
 * Respuesta completa de la fuente (HU #14087 AC8 ajustado): el proto-JSON del *Response del rpc, con los nombres snake_case
 * del .proto y los valores por defecto (false, 0, "", []). Ausente o null en consultas anteriores al cambio, reutilizadas sin
 * respuesta o llenadas a mano.
 */
export type RespuestaFuenteCompleta = Record<string, unknown>;

export interface DetalleCelda {
  id: string;
  vehiculo_id: string;
  /** Placa del vehículo o, si no tiene, su VIN (HU #14087). */
  identificador: string;
  /** Nombre del lote (diagnóstico) al que pertenece la celda (HU #14087). */
  nombre_diagnostico: string;
  fuente: Fuente;
  estado: EstadoCelda;
  motivo_na: MotivoNa | null;
  tipo_error: string | null;
  dato_obtenido_en: string | null;
  reutilizada: boolean;
  /** Solo RUNT, SIMIT_VEHICULO, AVALUO e IMPUESTO_DEPARTAMENTAL con la celda COMPLETADO; se discrimina por `fuente`. */
  resultado: ResultadoRuntDetalle | ResultadoSimitDetalle | ResultadoAvaluoDetalle | ResultadoImpuestoDetalle | null;
  /** Solo en las fuentes de persona; una entrada por persona del rol, en orden. */
  personas: PersonaDetalle[] | null;
  /** Respuesta completa de la fuente (HU #14087 AC8 ajustado); si falta, el detalle muestra `resultado` con un aviso. */
  respuesta_fuente?: RespuestaFuenteCompleta | null;
  /**
   * Respuesta íntegra del proveedor a nivel de celda (HU #14183 / #14184). Misma semántica que
   * `PersonaDetalle.respuesta_proveedor`. Solo se llena en SIMIT_VEHICULO; en propietario y locatario vive en
   * `personas[].respuesta_proveedor`. Null si no hay crudo, no es JSON o no es un objeto.
   */
  respuesta_proveedor?: RespuestaProveedor | null;
  /** Impuesto en ERROR con dato parcial: se rotula y no se muestra el monto. Ausente si es false. */
  dato_parcial?: boolean;
  /** HU #14563 B5. Auditoría saneada; ausente o null en el API anterior o sin intentos no manuales. */
  auditoria?: AuditoriaConsulta | null;
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

/** Código de fuente válido (la ruta de la página trae el segmento crudo). */
export const esFuente = (valor: string): valor is Fuente => FUENTES.some((f) => f.codigo === valor);

/** Página del detalle en pestaña nueva (HU #14087): misma forma que la ruta de la API, bajo /diagnosticos. */
export const rutaPaginaDetalle = (loteId: string, vehiculoId: string, fuente: Fuente) => rutaDetalleCelda(loteId, vehiculoId, fuente);

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
  AUTENTICACION: "El proveedor rechazó las credenciales de FLIT. Avísale al administrador para que las renueve; luego puedes reprocesar la consulta.",
  LIMITE_PROVEEDOR: "El proveedor limitó las consultas por ahora. Se reintentará automáticamente.",
  NO_CONECTADO: "Esta fuente no está conectada en este ambiente.",
  PERSONAS_CON_ERROR: "Algunas personas no se pudieron consultar; lo que sí respondió se muestra abajo.",
  BLOQUEADA_POR_RUNT: "Depende del RUNT del vehículo, que también falló. Se resuelve al reprocesar el RUNT.",
  SIN_PLACA: "El vehículo no tiene placa según el RUNT: no se puede consultar.",
  SIN_IDENTIFICADOR: "El vehículo no tiene ni placa ni VIN según el RUNT: no se puede consultar.",
  NO_CONFIRMADO: "El impuesto no quedó confirmado.",
  MODELO_NO_ACEPTADO: "El modelo del vehículo no es aceptado para consultar el impuesto.",
  DOCUMENTO_NO_COINCIDE: "El documento del propietario no coincide con el del impuesto.",
  DOCUMENTO_NO_HOMOLOGADO: "El tipo de documento del propietario no está homologado para el impuesto.",
  VEHICULO_NO_ENCONTRADO: "El vehículo no aparece en el impuesto.",
  VEHICULO_NO_CONSULTABLE: "El vehículo no se puede consultar en el impuesto.",
  REQUIERE_ACTUALIZACION: "El impuesto pide actualizar los datos del vehículo antes de consultar.",
  SIN_DOCUMENTO: "Falta el documento del propietario para consultar el impuesto.",
  SIN_MODELO: "Falta el modelo del vehículo para consultar el impuesto.",
  ORGANISMO_INCOMPLETO: "El organismo de tránsito no tiene municipio para consultar el impuesto.",
  SIN_NOMBRE_PROPIETARIO: "Falta el nombre del propietario para consultar el impuesto.",
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

/**
 * Fuentes cuyas personas pintan la respuesta íntegra del proveedor (HU #14154 y HU #14184): RUNT de persona, RUES en los
 * NIT, y SIMIT de propietario y locatario. El vehículo SIMIT pinta `DetalleCelda.respuesta_proveedor`, no esta lista.
 */
export const FUENTES_RESPUESTA_PROVEEDOR: ReadonlySet<Fuente> = new Set<Fuente>([
  "RUNT_PROPIETARIO",
  "RUNT_LOCATARIO",
  "SIMIT_PROPIETARIO",
  "SIMIT_LOCATARIO",
]);
