// Tipos de liquidaciones tal como los define contracts/openapi/impuestos.v1.yaml 1.1.0 (componentes ResumenLiquidacion,
// Liquidacion, LiquidacionConItems, Item, ItemDetalle, Periodo, Recibo y AnalisisCarga). Un monto `null` es «sin dato»
// y nunca suma como 0. Ninguna respuesta trae dirección, correo ni teléfono del propietario.

export type TipoLiquidacion = "INDIVIDUAL" | "MASIVA";

export type EstadoLiquidacion = "CREADO" | "EN_PROCESO" | "COMPLETADO" | "COMPLETADO_CON_NOVEDADES" | "CERRADO_MANUALMENTE";

export type EstadoItem =
  | "PENDIENTE"
  | "CONSULTANDO_RUNT"
  | "REINTENTO_PROGRAMADO"
  | "ENVIANDO"
  | "ENVIADO"
  | "INCIERTO"
  | "INCIERTO_REQUIERE_DECISION"
  | "DATOS_INCOMPLETOS"
  | "DEPARTAMENTO_NO_HABILITADO"
  | "EXITOSO"
  | "ERROR"
  | "SIN_RESULTADO_LOTE_CERRADO";

export type ResultadoStatus = "con_deuda" | "paz_y_salvo" | "error" | null;
export type ClaseError = "CORREGIR_DATOS" | "DEPENDE" | "REINTENTABLE" | null;
export type EstadoRecibo = "PENDIENTE" | "GUARDADO" | "SIN_ARCHIVO" | "PENDIENTE_REINTENTO_MANUAL";
export type MotivoRecibo = "SIN_FILE_ID" | "FILE_MISSING" | "NOT_FOUND" | "STORAGE_UNAVAILABLE" | null;
export type TipoRecibo = "VIGENCIA" | "CONSOLIDADO" | "PAZ_Y_SALVO";
export type MotivoFilaInvalida = "PLACA_VACIA" | "PLACA_INVALIDA";

export interface Referencia {
  id: string;
  nombre: string;
}

export interface Contadores {
  exitosas: number;
  conDeuda: number;
  pazYSalvo: number;
  error: number;
  datosIncompletos: number;
  departamentoNoHabilitado: number;
  incierto: number;
  sinResultado: number;
  pendientes: number;
}

export interface ResumenLiquidacion {
  id: string;
  tipo: TipoLiquidacion;
  estado: EstadoLiquidacion;
  totalItems: number;
  contadores: Contadores;
  creadaEn: string;
  creadaPor: Referencia;
  empresa: Referencia;
  nombreArchivo?: string | null;
  /** Solo en INDIVIDUAL. */
  placa?: string | null;
  /** Solo en INDIVIDUAL (1.1.0, delta 4): id de su único ítem, para enlazar directo al detalle de la placa. */
  itemId?: string | null;
  /** Solo en INDIVIDUAL (1.1.0, delta 4): estado de su único ítem; null en MASIVA. */
  estadoItem?: EstadoItem | null;
}

export interface Liquidacion extends ResumenLiquidacion {
  terminadaEn?: string | null;
  cerradaPor?: Referencia | null;
  cerradaEn?: string | null;
  /** Motivo del cierre manual (1.1.0, delta 5); null si no hubo cierre o no se dio. Texto plano. */
  motivoCierre?: string | null;
  configCopia: { maxRegistrosLote: number; departamentosHabilitados: string[] | null; concurrencia: number };
}

export interface Propietario {
  nombre: string | null;
  documentoTipo: "CC" | "NIT" | "CE" | "PA" | "TI" | "CD" | null;
  documentoNumero: string | null;
}

export interface Departamento {
  codigo: string;
  nombre: string;
  activa?: boolean;
}

/** Secretaría de tránsito de la placa según el RUNT (1.1.0, delta 5); null si el RUNT no la trajo. */
export interface Secretaria {
  nombre: string | null;
  ciudad: string | null;
}

export interface Item {
  id: string;
  liquidacionId: string;
  fila?: number | null;
  placa: string;
  estado: EstadoItem;
  errorCodigo: string | null;
  errorClase: ClaseError;
  errorMensaje?: string | null;
  campoFaltante: string | null;
  departamento: Departamento | null;
  propietario: Propietario | null;
  totalAPagar: number | null;
  resultadoStatus: ResultadoStatus;
  parcial: boolean;
  posibleDuplicado: boolean;
  resultadoTardio?: boolean;
  relanzadoDesde?: string | null;
  /** Ítem original del que se relanzó (1.1.0, delta 5; mismo valor que `relanzadoDesde`). */
  relanzadoDesdeItemId?: string | null;
  secretaria?: Secretaria | null;
}

export interface LiquidacionConItems extends Liquidacion {
  items: Item[];
}

export interface Pagina<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface Periodo {
  periodo: string;
  avaluo: number | null;
  impuesto: number | null;
  sancion: number | null;
  intereses: number | null;
  descuentos: number | null;
  pagosPrevios: number | null;
  totalAPagar: number | null;
  otrosCargos: Array<{ descripcion: string; monto: number | null }>;
  vencimiento: string | null;
  referenciaPago: string | null;
  noLiquidable: boolean;
  reciboId?: string | null;
}

export interface Recibo {
  id: string;
  tipo: TipoRecibo;
  periodos: string[];
  estado: EstadoRecibo;
  motivo: MotivoRecibo;
  razon?: string | null;
}

export interface ItemDetalle extends Item {
  empresa: Referencia;
  creadaPor: Referencia;
  creadaEn: string;
  consultaStatus?: "succeeded" | "partial" | "failed" | null;
  /** `result` del proveedor tal cual (guía §6): warnings, validations, file_errors… null sin resultado. */
  resultado: Record<string, unknown> | null;
  periodos: Periodo[];
  recibos: Recibo[];
}

export interface AnalisisCarga {
  validas: number;
  duplicadas: number;
  invalidas: Array<{ fila: number; valor: string; motivo: MotivoFilaInvalida }>;
  tope: number;
  superaTope: boolean;
}

export interface FiltrosLiquidaciones {
  tipo: TipoLiquidacion | null;
  estado: EstadoLiquidacion | null;
  desde: string;
  hasta: string;
  placa: string;
  page: number;
  pageSize: number;
}
