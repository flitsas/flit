// Mapa único estado → chip de liquidaciones (spec-visual §1) con los literales cerrados del contrato
// contracts/openapi/impuestos.v1.yaml 1.1.0 (`x-flit-etiquetas` de cada enum y `x-flit-literales`), que mandan ante un
// choque con la spec. Un código desconocido se pinta en `neutral` como «Sin clasificar», nunca con el código crudo.
import type { StatusBadgeTone } from "@flit/ui/StatusBadge";
import { ERRORES_EMPRESA } from "@/lib/errores";
import { rotular } from "@/lib/rotulos";
import { bannersDeSesion, type SesionImpuestos } from "@/lib/sesion";
import type { ClaseError, Contadores, EstadoItem, EstadoLiquidacion, Item, MotivoRecibo, Recibo, ResumenLiquidacion } from "./tipos";

/** Chip: texto y tono; `enProceso` se pinta con EtiquetaEnProceso (punto pulsante). */
export interface Chip {
  texto: string;
  tono: StatusBadgeTone;
  enProceso?: boolean;
}

/** x-flit-literales.errores del contrato (solo los de liquidaciones y recibos). `{tope}` lo sustituye mensajeErrorFijo. */
export const ERRORES_LIQUIDACIONES: Readonly<Record<string, string>> = {
  ...ERRORES_EMPRESA,
  APROVISIONAMIENTO_NO_LISTO: "La compañía todavía no puede liquidar porque su configuración con el proveedor no está lista.",
  PRODUCTO_SUSPENDIDO: "Impuestos está suspendido para la compañía: solo lectura.",
  PLACA_INVALIDA: "Escribe una placa válida: de 4 a 10 letras o números.",
  FORMATO_NO_SOPORTADO: "Solo se admiten archivos CSV y XLSX.",
  ARCHIVO_DEMASIADO_GRANDE: "El archivo supera el máximo de 5 MB.",
  COLUMNA_PLACA_AUSENTE: "Falta la columna de placa.",
  ARCHIVO_SIN_PLACAS: "El archivo no tiene placas válidas.",
  LIMITE_SUPERADO: "El archivo supera el tope de {tope} placas por lote.",
  LIQUIDACION_NO_ENCONTRADA: "La liquidación no existe.",
  ITEM_NO_ENCONTRADO: "La placa no existe en esta liquidación.",
  RECIBO_NO_ENCONTRADO: "El recibo no existe.",
  RECIBO_NO_DISPONIBLE: "El recibo no está disponible.",
  YA_TERMINADA: "La liquidación ya terminó: no se puede cerrar.",
  NO_RELANZABLE: "Solo se relanzan placas en «Incierto — requiere decisión» o con error reintentable.",
  CONFIRMACION_REQUERIDA: "Confirma que aceptas un posible duplicado en el proveedor.",
  RANGO_INVALIDO: "El rango de fechas no es válido.",
  FILTRO_INVALIDO: "Un filtro no es válido.",
  PAGINACION_INVALIDA: "La paginación no es válida.",
  DEMASIADAS_SOLICITUDES: "Hiciste demasiadas solicitudes. Espera un momento e intenta de nuevo.",
};

/** x-flit-literales.pantalla del contrato que usa este módulo. */
export const PANTALLA = {
  marcaParcial: "Parcial",
  marcaPosibleDuplicado: "Posible duplicado",
  marcaResultadoTardio: "Resultado tardío",
  accionLiquidarPlaca: "Liquidar placa",
  accionCargaMasiva: "Carga masiva",
  accionCerrarLote: "Cerrar lote",
  accionRelanzar: "Relanzar",
  accionDescargarRecibo: "Descargar recibo",
  accionDescargarPlantilla: "Descargar plantilla",
  confirmarCerrarLote:
    "Cerrar el lote deja las placas pendientes en «Sin resultado (lote cerrado)». Si después llega un resultado, se aplica a la placa sin reabrir el lote.",
  confirmarRelanzar:
    "Relanzar crea una liquidación nueva para la placa. El proveedor puede haber recibido la solicitud anterior: podría quedar duplicada.",
  detalleIncierto: "Estamos verificando con el proveedor si recibió la solicitud.",
  sinResultados: "No hay resultados para los filtros elegidos.",
} as const;

// ── Etiquetas (x-flit-etiquetas) ──────────────────────────────────────────────────────────────────────────────────────

export const TIPOS: Readonly<Record<string, string>> = { INDIVIDUAL: "Individual", MASIVA: "Masiva" };

export const ESTADOS_LIQUIDACION: Readonly<Record<EstadoLiquidacion, string>> = {
  CREADO: "Creado",
  EN_PROCESO: "En proceso",
  COMPLETADO: "Completado",
  COMPLETADO_CON_NOVEDADES: "Completado con novedades",
  CERRADO_MANUALMENTE: "Cerrado manualmente",
};

export const ESTADOS_ITEM: Readonly<Record<EstadoItem, string>> = {
  PENDIENTE: "Pendiente",
  CONSULTANDO_RUNT: "Consultando RUNT",
  REINTENTO_PROGRAMADO: "Reintento programado",
  ENVIANDO: "Enviando",
  ENVIADO: "Esperando resultado",
  INCIERTO: "Incierto",
  INCIERTO_REQUIERE_DECISION: "Incierto — requiere decisión",
  DATOS_INCOMPLETOS: "Datos incompletos",
  DEPARTAMENTO_NO_HABILITADO: "Departamento no habilitado",
  EXITOSO: "Exitosa",
  ERROR: "Con error",
  SIN_RESULTADO_LOTE_CERRADO: "Sin resultado (lote cerrado)",
};

export const RESULTADOS: Readonly<Record<string, string>> = { con_deuda: "Con deuda", paz_y_salvo: "Paz y salvo", error: "Con error" };

export const CLASES_ERROR: Readonly<Record<string, string>> = {
  CORREGIR_DATOS: "Corregir datos",
  DEPENDE: "Depende del mensaje",
  REINTENTABLE: "Reintentable como nueva consulta",
};

export const CAMPOS_FALTANTES: Readonly<Record<string, string>> = {
  documentType: "Tipo de documento del propietario",
  documentNumber: "Número de documento del propietario",
  OwnerName: "Nombre del propietario",
  vehicleModel: "Modelo del vehículo",
  secretaryDepartmentCode: "Secretaría de tránsito sin homologar",
  secretaryCity: "Municipio de la secretaría de tránsito",
  OwnerEmail: "Correo del propietario",
  OwnerPhone: "Celular o teléfono del propietario",
  OwnerAddress: "Dirección del propietario",
};

export const ESTADOS_RECIBO: Readonly<Record<string, string>> = {
  PENDIENTE: "Descargando",
  GUARDADO: "Disponible",
  SIN_ARCHIVO: "Sin recibo",
  PENDIENTE_REINTENTO_MANUAL: "No disponible",
};

export const MOTIVOS_RECIBO: Readonly<Record<string, string>> = {
  SIN_FILE_ID: "Sin recibo",
  FILE_MISSING: "El archivo no existe en el proveedor",
  NOT_FOUND: "El proveedor no encontró el archivo",
  STORAGE_UNAVAILABLE: "Almacenamiento del proveedor no disponible, intentar más tarde",
};

export const TIPOS_RECIBO: Readonly<Record<string, string>> = {
  VIGENCIA: "Recibo de vigencia",
  CONSOLIDADO: "Recibo consolidado",
  PAZ_Y_SALVO: "Paz y salvo",
};

export const MOTIVOS_FILA: Readonly<Record<string, string>> = {
  PLACA_VACIA: "Fila sin placa",
  PLACA_INVALIDA: "Placa no válida (de 4 a 10 letras o números)",
};

/** Periodo.x-flit-etiquetas (columnas de la tabla de vigencias). */
export const COLUMNAS_PERIODO = {
  periodo: "Vigencia",
  avaluo: "Avalúo",
  impuesto: "Impuesto",
  sancion: "Sanción",
  intereses: "Intereses",
  descuentos: "Descuentos",
  pagosPrevios: "Pagos previos",
  totalAPagar: "Total a pagar",
  otrosCargos: "Otros cargos",
  vencimiento: "Vencimiento",
  referenciaPago: "Referencia de pago",
  noLiquidable: "El portal no permite liquidar esta vigencia",
} as const;

/** Contadores.x-flit-etiquetas. */
export const CONTADORES: Readonly<Record<keyof Contadores, string>> = {
  exitosas: "Exitosas",
  conDeuda: "Con deuda",
  pazYSalvo: "Paz y salvo",
  error: "Con error",
  datosIncompletos: "Datos incompletos",
  departamentoNoHabilitado: "Departamento no habilitado",
  incierto: "Inciertas",
  sinResultado: "Sin resultado",
  pendientes: "Pendientes",
};

// ── Chips ──────────────────────────────────────────────────────────────────────────────────────────────────────────────

const TONO_LOTE: Readonly<Record<EstadoLiquidacion, StatusBadgeTone>> = {
  CREADO: "neutral",
  EN_PROCESO: "info",
  COMPLETADO: "success",
  COMPLETADO_CON_NOVEDADES: "warning",
  CERRADO_MANUALMENTE: "tinta",
};

const TONO_ITEM: Readonly<Record<EstadoItem, StatusBadgeTone>> = {
  PENDIENTE: "info",
  CONSULTANDO_RUNT: "info",
  REINTENTO_PROGRAMADO: "purple",
  ENVIANDO: "info",
  ENVIADO: "info",
  INCIERTO: "purple",
  INCIERTO_REQUIERE_DECISION: "warning",
  DATOS_INCOMPLETOS: "warning",
  DEPARTAMENTO_NO_HABILITADO: "neutral",
  EXITOSO: "success",
  ERROR: "danger",
  SIN_RESULTADO_LOTE_CERRADO: "neutral",
};

const tiene = <K extends string>(mapa: Readonly<Record<K, unknown>>, clave: string | null | undefined): clave is K =>
  Boolean(clave) && Object.prototype.hasOwnProperty.call(mapa, clave as string);

const SIN_CLASIFICAR = (contexto: string, codigo: string | null | undefined): Chip => ({
  texto: rotular({ contexto, codigo, respaldo: {} }),
  tono: "neutral",
});

/** Chip del lote (§1.1); EN_PROCESO va con EtiquetaEnProceso. */
export function chipLote(estado: string | null | undefined): Chip {
  if (!tiene(ESTADOS_LIQUIDACION, estado)) return SIN_CLASIFICAR("estado del lote", estado);
  return { texto: ESTADOS_LIQUIDACION[estado], tono: TONO_LOTE[estado], enProceso: estado === "EN_PROCESO" };
}

/** Chip de la placa no inscrita (detalle y grilla del lote): no es un error para el usuario. */
export const CHIP_PLACA_NO_INSCRITA = "Con novedad";

/**
 * Chip del ítem (§1.2). Un EXITOSO muestra el resultado: «Con deuda» (warning) o «Paz y salvo» (success). La placa no
 * inscrita (`esPlacaNoInscrita`) dice «Con novedad» en `warning`.
 */
export function chipItem(item: Pick<Item, "estado" | "resultadoStatus"> & Partial<Pick<Item, "errorCodigo" | "errorMensaje">>): Chip {
  const { estado, resultadoStatus } = item;
  if (!tiene(ESTADOS_ITEM, estado)) return SIN_CLASIFICAR("estado de la placa", estado);
  if (estado === "EXITOSO" && resultadoStatus === "con_deuda") return { texto: RESULTADOS.con_deuda, tono: "warning" };
  if (estado === "EXITOSO" && resultadoStatus === "paz_y_salvo") return { texto: RESULTADOS.paz_y_salvo, tono: "success" };
  if (esPlacaNoInscrita(item)) return { texto: CHIP_PLACA_NO_INSCRITA, tono: "warning" };
  return { texto: ESTADOS_ITEM[estado], tono: TONO_ITEM[estado] };
}

/**
 * Chip de una fila del grid. La masiva lleva el del lote; la individual, el de su única placa: `estadoItem` (contrato
 * 1.1.0, delta 4) y, en EXITOSO, el resultado que dicen los contadores (el resumen no trae `resultadoStatus`). Una
 * respuesta sin `estadoItem` (backend anterior a la 1.1.0) lo deduce de los contadores y, si no deciden, el del lote.
 */
export function chipFila(r: Pick<ResumenLiquidacion, "tipo" | "estado" | "contadores" | "estadoItem">): Chip {
  if (r.tipo !== "INDIVIDUAL") return chipLote(r.estado);
  if (r.estadoItem) {
    const c = r.contadores;
    const resultadoStatus = c && c.conDeuda > 0 ? "con_deuda" : c && c.pazYSalvo > 0 ? "paz_y_salvo" : null;
    return chipItem({ estado: r.estadoItem, resultadoStatus });
  }
  if (!r.contadores) return chipLote(r.estado);
  const c = r.contadores;
  if (c.pendientes > 0) return chipLote(r.estado);
  if (c.conDeuda > 0) return { texto: RESULTADOS.con_deuda, tono: "warning" };
  if (c.pazYSalvo > 0) return { texto: RESULTADOS.paz_y_salvo, tono: "success" };
  if (c.exitosas > 0) return { texto: ESTADOS_ITEM.EXITOSO, tono: "success" };
  if (c.error > 0) return { texto: ESTADOS_ITEM.ERROR, tono: "danger" };
  if (c.datosIncompletos > 0) return { texto: ESTADOS_ITEM.DATOS_INCOMPLETOS, tono: "warning" };
  if (c.departamentoNoHabilitado > 0) return { texto: ESTADOS_ITEM.DEPARTAMENTO_NO_HABILITADO, tono: "neutral" };
  if (c.incierto > 0) return { texto: ESTADOS_ITEM.INCIERTO, tono: "purple" };
  if (c.sinResultado > 0) return { texto: ESTADOS_ITEM.SIN_RESULTADO_LOTE_CERRADO, tono: "neutral" };
  return chipLote(r.estado);
}

export function chipRecibo(estado: string | null | undefined): Chip {
  const tonos: Record<string, StatusBadgeTone> = { GUARDADO: "success", PENDIENTE: "info", SIN_ARCHIVO: "neutral", PENDIENTE_REINTENTO_MANUAL: "warning" };
  if (!tiene(ESTADOS_RECIBO, estado)) return SIN_CLASIFICAR("estado del recibo", estado);
  return { texto: ESTADOS_RECIBO[estado], tono: tonos[estado] };
}

export const nombreTipo = (tipo: string | null | undefined) => rotular({ contexto: "tipo de liquidación", codigo: tipo, respaldo: TIPOS });
export const nombreClaseError = (clase: ClaseError | string | null | undefined) =>
  rotular({ contexto: "clase de error", codigo: clase, respaldo: CLASES_ERROR });
export const nombreCampoFaltante = (campo: string | null | undefined) =>
  rotular({ contexto: "campo faltante", codigo: campo, respaldo: CAMPOS_FALTANTES });
export const nombreMotivoFila = (motivo: string | null | undefined) => rotular({ contexto: "motivo de fila", codigo: motivo, respaldo: MOTIVOS_FILA });

/** Nombre del recibo: «Recibo de vigencia 2025», «Recibo consolidado» o «Paz y salvo». */
export function nombreRecibo(r: Pick<Recibo, "tipo" | "periodos">): string {
  const base = rotular({ contexto: "tipo de recibo", codigo: r.tipo, respaldo: TIPOS_RECIBO });
  return r.tipo === "VIGENCIA" && r.periodos.length > 0 ? `${base} ${r.periodos.join(", ")}` : base;
}

/** Motivo de un recibo sin archivo; con SIN_FILE_ID agrega la razón del warning: «Sin recibo: <razón>». */
export function motivoRecibo(motivo: MotivoRecibo | string | null | undefined, razon?: string | null): string | null {
  if (!motivo) return null;
  const texto = rotular({ contexto: "motivo del recibo", codigo: motivo, respaldo: MOTIVOS_RECIBO });
  return motivo === "SIN_FILE_ID" && razon && razon.trim() ? `${texto}: ${razon.trim()}` : texto;
}

// ── Reglas ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

const EN_CURSO: ReadonlySet<string> = new Set(["PENDIENTE", "CONSULTANDO_RUNT", "REINTENTO_PROGRAMADO", "ENVIANDO", "ENVIADO", "INCIERTO"]);

/** La placa todavía espera resultado (plan §3.2): la pantalla se sondea. */
export const itemEnCurso = (estado: string) => EN_CURSO.has(estado);

/** El lote sigue abierto (CREADO o EN_PROCESO): se sondea y se puede cerrar a mano (DA-6b). */
export const loteAbierto = (estado: string) => estado === "CREADO" || estado === "EN_PROCESO";

type ErrorDelItem = Pick<Item, "estado"> & Partial<Pick<Item, "errorCodigo" | "errorMensaje">>;

/**
 * «Placa no inscrita» (spec-visual §1.3): ERROR con `portal_rejected` y un mensaje del proveedor que dice «no se encuentra
 * inscrita» (sin distinguir mayúsculas). No es una falla: el portal no tiene la placa o no le queda nada por liquidar. Se
 * pinta como advertencia «Errores encontrados» y se puede relanzar.
 */
export function esPlacaNoInscrita(item: ErrorDelItem): boolean {
  return item.estado === "ERROR" && item.errorCodigo === "portal_rejected" && Boolean(item.errorMensaje?.toLowerCase().includes("no se encuentra inscrita"));
}

/** Título del aviso de la placa no inscrita (§3.6). */
export const TITULO_PLACA_NO_INSCRITA = "Errores encontrados";

const FINAL_BASE_DE_DATOS = "en nuestra base de datos.";
const FINAL_SIN_VIGENCIAS = " o no tiene vigencias pendientes.";

/** Mensaje del proveedor con el final ajustado: «…en nuestra base de datos o no tiene vigencias pendientes.». */
export function mensajePlacaNoInscrita(mensaje: string): string {
  const texto = mensaje.trim();
  if (texto.endsWith(FINAL_BASE_DE_DATOS)) return `${texto.slice(0, -1)}${FINAL_SIN_VIGENCIAS}`;
  return `${texto.replace(/\.+$/, "")}${FINAL_SIN_VIGENCIAS}`;
}

/** Respaldo del aviso de ERROR cuando no hay mensaje del proveedor ni texto propio para el código. */
export const MENSAJE_ERROR_RESPALDO = "El proveedor no pudo liquidar esta placa.";

const PUEDES_MAS_TARDE = "Puedes relanzar la placa más tarde.";

/**
 * Texto propio de FLIT por `errorCodigo` (tabla de clases del backend, `ClasificacionErrores.Tabla`), para cuando el
 * proveedor no manda `errorMensaje` (p. ej. 409 `source_inactive` al enviar). Recibe el departamento del ítem, o null.
 */
export const MENSAJES_ERROR: Readonly<Record<string, (departamento: string | null) => string>> = {
  invalid_params: () => "Los datos enviados para esta placa no son válidos. Revísalos y crea una nueva liquidación.",
  owner_validation_failed: () => "Los datos del propietario no coinciden con los del portal del departamento. Revísalos y crea una nueva liquidación.",
  vehicle_data_required: () => "El portal del departamento pide datos del vehículo que no tenemos. Complétalos y crea una nueva liquidación.",
  portal_rejected: () => "El portal del departamento rechazó la consulta de esta placa.",
  captcha_blocked: () => "El portal del departamento bloqueó el ingreso. Relánzala en unos minutos.",
  auth_failed: () => `No se pudo ingresar al portal del departamento. ${PUEDES_MAS_TARDE}`,
  rate_limited: () => "El portal del departamento recibió demasiadas consultas. Relánzala en unos minutos.",
  timeout: () => "El portal del departamento tardó demasiado en responder. Puedes relanzar la placa.",
  http_status: () => `El portal del departamento respondió con una falla. ${PUEDES_MAS_TARDE}`,
  proxy_failed: () => `No se pudo conectar con el portal del departamento. ${PUEDES_MAS_TARDE}`,
  source_inactive: (departamento) =>
    `El proveedor tiene deshabilitado temporalmente ${departamento ? `el departamento de ${departamento}` : "este departamento"}. ${PUEDES_MAS_TARDE}`,
  adapter_unavailable: () => `La consulta para este departamento no está disponible por ahora. ${PUEDES_MAS_TARDE}`,
  parse_error: () => "No se pudo leer la respuesta del portal del departamento. Puedes relanzar la placa.",
  unexpected: () => "Ocurrió una falla inesperada al liquidar esta placa. Puedes relanzarla.",
  worker_lost: () => "La consulta se interrumpió antes de terminar. Puedes relanzar la placa.",
};

/**
 * Cuerpo del aviso de ERROR (§1.3): el `errorMensaje` del proveedor si lo trae; si no, el texto propio del código
 * (`MENSAJES_ERROR`); si el código no se conoce, el respaldo. La placa no inscrita lleva además el final ajustado.
 */
export function mensajeError(item: ErrorDelItem & Partial<Pick<Item, "departamento">>): string {
  const delProveedor = item.errorMensaje?.trim();
  if (delProveedor) return esPlacaNoInscrita(item) ? mensajePlacaNoInscrita(delProveedor) : delProveedor;
  const codigo = item.errorCodigo ?? "";
  const propio = Object.prototype.hasOwnProperty.call(MENSAJES_ERROR, codigo) ? MENSAJES_ERROR[codigo] : null;
  return propio ? propio(item.departamento?.nombre?.trim() || null) : MENSAJE_ERROR_RESPALDO;
}

/**
 * Relanzable (plan §3.2): INCIERTO_REQUIERE_DECISION, DATOS_INCOMPLETOS, ERROR con clase REINTENTABLE y la placa no
 * inscrita (`esPlacaNoInscrita`).
 */
export function esRelanzable(item: Pick<Item, "estado" | "errorClase"> & Partial<Pick<Item, "errorCodigo" | "errorMensaje">>): boolean {
  if (item.estado === "INCIERTO_REQUIERE_DECISION" || item.estado === "DATOS_INCOMPLETOS") return true;
  return item.estado === "ERROR" && (item.errorClase === "REINTENTABLE" || esPlacaNoInscrita(item));
}

/** Placas terminadas del lote para el avance: el total menos las pendientes (supuesto: `pendientes` cubre los no finales). */
export function terminadas(r: Pick<ResumenLiquidacion, "totalItems" | "contadores">): number {
  return Math.max(0, r.totalItems - (r.contadores?.pendientes ?? 0));
}

type SesionConLiquidar = SesionImpuestos & {
  aprovisionamiento?: (SesionImpuestos["aprovisionamiento"] & { puedeLiquidar?: boolean | null; motivoNoLiquida?: string | null }) | null;
};

/**
 * Motivo por el que no se puede crear ni relanzar (Sesion.aprovisionamiento.puedeLiquidar=false con su motivoNoLiquida,
 * o, si el back no lo trae, el banner SUSPENDIDO / NO_LISTO). null = se puede. Es el literal del contrato.
 */
export function bloqueoCreacion(sesion: SesionImpuestos | null | undefined): string | null {
  const a = (sesion as SesionConLiquidar | null | undefined)?.aprovisionamiento;
  if (a?.puedeLiquidar === false) {
    const motivo = a.motivoNoLiquida === "PRODUCTO_SUSPENDIDO" ? "PRODUCTO_SUSPENDIDO" : "APROVISIONAMIENTO_NO_LISTO";
    return ERRORES_LIQUIDACIONES[motivo];
  }
  if (a?.puedeLiquidar === true) return null;
  const banners = bannersDeSesion(sesion);
  if (banners.includes("SUSPENDIDO")) return ERRORES_LIQUIDACIONES.PRODUCTO_SUSPENDIDO;
  if (banners.includes("NO_LISTO")) return ERRORES_LIQUIDACIONES.APROVISIONAMIENTO_NO_LISTO;
  return null;
}
