// Etiquetas y colores de los estados (especificación GDC §4.9). Funciones puras: las usan la bandeja, el detalle y las
// pruebas. Colores: sin procesar gris; DP o solicitud en curso azul; favorable o aceptado verde; vencido, desfavorable o
// rechazado rojo; inactivo gris claro.
import type { StatusTone } from "@flit/ui";

export interface Etiqueta {
  texto: string;
  tono: StatusTone;
}

const NEUTRO: Etiqueta = { texto: "—", tono: "neutral" };

function de(mapa: Record<string, Etiqueta>, valor: string | null | undefined): Etiqueta {
  return (valor && mapa[valor]) || (valor ? { texto: valor, tono: "neutral" } : NEUTRO);
}

export const ESTADO_LEGAL: Record<string, Etiqueta> = {
  SinNotificar: { texto: "Sin notificación", tono: "neutral" },
  EnTiempo: { texto: "En tiempo", tono: "success" },
  Vencido: { texto: "Vencido", tono: "danger" },
  Caducado: { texto: "Caducado", tono: "warning" },
  Prescrito: { texto: "Prescrito", tono: "info" },
};

export const ESTADO_PROCESO: Record<string, Etiqueta> = {
  SinProcesar: { texto: "Sin procesar", tono: "neutral" },
  DerechoPeticion: { texto: "Derecho de petición", tono: "info" },
  RespuestaFavorable: { texto: "Respuesta OT: favorable", tono: "success" },
  RespuestaDesfavorable: { texto: "Respuesta OT: desfavorable", tono: "danger" },
  NoEncontrado: { texto: "No encontrado en fuente", tono: "neutral" },
  Inactivado: { texto: "Inactivado", tono: "neutral" },
};

export const ULTIMO_AVISO: Record<string, Etiqueta> = {
  SinNotificar: { texto: "Sin aviso", tono: "neutral" },
  Aviso1: { texto: "Aviso 1", tono: "info" },
  Aviso2: { texto: "Aviso 2", tono: "info" },
  Aviso3: { texto: "Aviso 3", tono: "warning" },
};

export const CAMBIO_CONTRAVENTOR: Record<string, Etiqueta> = {
  NoAplica: { texto: "No aplica", tono: "neutral" },
  SinSolicitar: { texto: "Sin solicitar", tono: "neutral" },
  Solicitado: { texto: "En proceso", tono: "info" },
  RechazadoOt: { texto: "Rechazado OT", tono: "danger" },
  AceptadoOt: { texto: "Exitoso", tono: "success" },
};

export const VENTANA_DESCUENTO: Record<string, Etiqueta> = {
  Descuento50: { texto: "Descuento 50%", tono: "success" },
  Descuento25: { texto: "Descuento 25%", tono: "warning" },
  SinDescuento: { texto: "Sin descuento", tono: "danger" },
  SinFechaBase: { texto: "Sin fecha base", tono: "neutral" },
};

export const FUENTE: Record<string, Etiqueta> = {
  Simit: { texto: "SIMIT", tono: "info" },
  Municipal: { texto: "Municipal", tono: "info" },
  Ocr: { texto: "OCR", tono: "neutral" },
  MigracionV1: { texto: "V1", tono: "neutral" },
};

export const CONSULTA_CONTRAVENTOR: Record<string, Etiqueta> = {
  NoAplica: { texto: "Sin conductor", tono: "neutral" },
  Pendiente: { texto: "Pendiente", tono: "warning" },
  Encontrado: { texto: "Encontrado", tono: "success" },
  NoEncontrado: { texto: "No encontrado", tono: "danger" },
  Error: { texto: "Error API", tono: "danger" },
};

export const RESULTADO_DP: Record<string, Etiqueta> = {
  Favorable: { texto: "Favorable", tono: "success" },
  Desfavorable: { texto: "Desfavorable", tono: "danger" },
};

export const ESTADO_DP: Record<string, Etiqueta> = {
  Enviando: { texto: "Enviando", tono: "info" },
  Enviado: { texto: "Enviado", tono: "info" },
  EnvioFallido: { texto: "Envío fallido", tono: "danger" },
  Respondido: { texto: "Respondido", tono: "success" },
};

export const CANAL: Record<string, Etiqueta> = {
  SimitNit: { texto: "SIMIT por NIT", tono: "info" },
  SimitPlaca: { texto: "SIMIT por placa", tono: "info" },
  MunicipalNit: { texto: "Municipal por NIT", tono: "info" },
  MunicipalPlaca: { texto: "Municipal por placa", tono: "info" },
};

export const RESULTADO_CICLO: Record<string, Etiqueta> = {
  EnCurso: { texto: "En curso", tono: "info" },
  Exitoso: { texto: "Exitoso", tono: "success" },
  Parcial: { texto: "Parcial", tono: "warning" },
  Fallido: { texto: "Fuente no disponible", tono: "danger" },
};

export const ESTADO_SOLICITUD: Record<string, Etiqueta> = {
  Solicitado: { texto: "Solicitado", tono: "info" },
  RechazadoOt: { texto: "Rechazado OT", tono: "danger" },
  AceptadoOt: { texto: "Aceptado OT", tono: "success" },
  EnvioFallido: { texto: "Envío fallido", tono: "danger" },
};

export const TRABAJO: Record<string, string> = {
  IngestaSimitNit: "Ingesta SIMIT por NIT",
  IngestaSimitPlaca: "Ingesta SIMIT por placa",
  IngestaMunicipalNit: "Ingesta municipal por NIT",
  IngestaMunicipalPlaca: "Ingesta municipal por placa",
  AvisosConductor: "Avisos al conductor",
  ReintentoContraventor: "Reintento de consulta de conductor",
  ReporteSemanal: "Reporte semanal",
  ReporteMensual: "Reporte mensual",
};

export const TIPO_ALERTA: Record<string, string> = {
  Todas: "Todas",
  SoloCriticas: "Solo críticas",
  SoloSimit: "Solo SIMIT",
  SoloMunicipal: "Solo municipal",
  SoloOcr: "Solo OCR",
};

export const EVENTO_TIMELINE: Record<string, string> = {
  Ingreso: "Ingreso",
  FuenteAdicional: "Otra fuente",
  DatosActualizados: "Datos actualizados",
  CambioFuentePrincipal: "Cambio de fuente",
  PlazosCalculados: "Plazos",
  InactivadoDesaparicion: "Desapareció de la fuente",
  Reactivado: "Reactivado",
  ReaparecioInactivoOperador: "Reapareció",
  InactivadoOperador: "Inactivado",
  ContraventorConsultado: "Consulta de conductor",
  ContraventorAsignado: "Conductor asignado",
  ContraventorReemplazado: "Conductor reemplazado",
  DocumentoConductorCargado: "Documento del conductor",
  AvisoEnviado: "Aviso enviado",
  ComunicacionEstado: "Estado del correo",
  PortalAbierto: "Portal abierto",
  CambioContraventorSolicitado: "Cambio solicitado",
  CambioContraventorRechazado: "Cambio rechazado",
  CambioContraventorAceptado: "Cambio aceptado",
  DpEnviado: "DP enviado",
  DpEnvioFallido: "DP no enviado",
  DpBloqueadoSinRl: "DP bloqueado",
  DpRespuestaRegistrada: "Respuesta del OT",
  DpFavorableAutomatico: "DP favorable",
  PagoRegistrado: "Pago",
  CobroCoactivoDetectado: "Cobro coactivo",
  NotaManual: "Observación",
};

export const ESTADO_COMUNICACION: Record<string, Etiqueta> = {
  Pendiente: { texto: "Pendiente", tono: "warning" },
  Enviado: { texto: "Enviado", tono: "info" },
  Entregado: { texto: "Entregado", tono: "success" },
  Abierto: { texto: "Abierto", tono: "success" },
  Rebotado: { texto: "Rebotado", tono: "danger" },
  Fallido: { texto: "Fallido", tono: "danger" },
};

export const TIPO_COMUNICACION: Record<string, string> = {
  Aviso1: "Aviso 1 al conductor",
  Aviso2: "Aviso 2 al conductor",
  Aviso3: "Aviso 3 al conductor",
  Aviso4: "Aviso 4 (cambio de contraventor)",
  DpOt: "Derecho de petición al OT",
  CambioContraventorOt: "Cambio de contraventor al OT",
  AlertaFalloIntegracion: "Alerta de integración",
  AlertaFalloOcr: "Alerta de OCR",
  AlertaSinRepresentante: "Alerta sin representante legal",
  ReporteSemanal: "Reporte semanal",
  ReporteMensual: "Reporte mensual",
  ExportacionLista: "Exportación lista",
};

export const TIPO_PLANTILLA: Record<string, string> = {
  DpDocumento: "Documento del derecho de petición",
  CartaCambioContraventor: "Carta al conductor (cambio de contraventor)",
  FormatoCambioContraventor: "Formato de cambio de contraventor",
  CorreoAviso1: "Correo: aviso 1",
  CorreoAviso2: "Correo: aviso 2",
  CorreoAviso3: "Correo: aviso 3",
  CorreoAviso4: "Correo: aviso 4",
  CorreoDpOt: "Correo: DP al OT",
  CorreoCambioContraventorOt: "Correo: cambio de contraventor al OT",
  CorreoAlertaFallo: "Correo: alerta de fallo",
  CorreoAlertaSinRl: "Correo: alerta sin representante",
  CorreoReporteSemanal: "Correo: reporte semanal",
  CorreoReporteMensual: "Correo: reporte mensual",
};

/** Las plantillas de correo se ven como HTML; las demás son documentos PDF. */
export const esPlantillaDeCorreo = (tipo: string) => tipo.startsWith("Correo");

export const estadoComunicacion = (v: string | null | undefined) => de(ESTADO_COMUNICACION, v);

export const estadoLegal = (v: string | null | undefined) => de(ESTADO_LEGAL, v);
export const estadoProceso = (v: string | null | undefined) => de(ESTADO_PROCESO, v);
export const ultimoAviso = (v: string | null | undefined) => de(ULTIMO_AVISO, v);
export const cambioContraventor = (v: string | null | undefined) => de(CAMBIO_CONTRAVENTOR, v);
export const ventanaDescuento = (v: string | null | undefined) => de(VENTANA_DESCUENTO, v);
export const fuente = (v: string | null | undefined) => de(FUENTE, v);
export const consultaContraventor = (v: string | null | undefined) => de(CONSULTA_CONTRAVENTOR, v);
export const resultadoDp = (v: string | null | undefined) => de(RESULTADO_DP, v);
export const estadoDp = (v: string | null | undefined) => de(ESTADO_DP, v);
export const canal = (v: string | null | undefined) => de(CANAL, v);
export const resultadoCiclo = (v: string | null | undefined) => de(RESULTADO_CICLO, v);
export const estadoSolicitud = (v: string | null | undefined) => de(ESTADO_SOLICITUD, v);

/**
 * Texto del semáforo legal: en tiempo dice cuántos días hábiles quedan («Vence hoy» el último día, «1 día hábil»,
 * «5 días hábiles»); los demás estados, su etiqueta.
 */
export function textoSemaforo(estado: string, diasRestantes: number | null | undefined): string {
  if (estado !== "EnTiempo" || diasRestantes === null || diasRestantes === undefined) return estadoLegal(estado).texto;
  if (diasRestantes <= 0) return "Vence hoy";
  return diasRestantes === 1 ? "1 día hábil" : `${diasRestantes} días hábiles`;
}

/** Tono del semáforo: en tiempo pero a 2 días hábiles o menos se pinta en ámbar (próximo a vencer). */
export function tonoSemaforo(estado: string, proximoAVencer: boolean): StatusTone {
  if (estado === "EnTiempo" && proximoAVencer) return "warning";
  return estadoLegal(estado).tono;
}

/** Opciones de un selector a partir de un mapa de etiquetas. */
export function opciones(mapa: Record<string, Etiqueta | string>): { valor: string; texto: string }[] {
  return Object.entries(mapa).map(([valor, e]) => ({ valor, texto: typeof e === "string" ? e : e.texto }));
}
