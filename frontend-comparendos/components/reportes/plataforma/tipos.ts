/** `GET /admin/reportes/plataforma` (solo SuperAdmin). Aquí, y solo aquí, se ve la fuente y el error técnico (R3). */
export interface ReportePlataforma {
  desde: string;
  hasta: string;
  fuentes: { fuente: string; consultas: number; conError: number; obtenidos: number; nuevos: number }[];
  porDia: { dia: string; fuente: string; consultas: number; conError: number; obtenidos: number; nuevos: number }[];
  /** Solo con «todas las empresas»: la consulta técnica no guarda la empresa. */
  erroresPorTipo: { fuente: string; tipo: string; cantidad: number }[] | null;
  correos: {
    fallidos: number;
    rebotados: number;
    motivos: { motivo: string; cantidad: number }[];
    recientes: { id: string; empresaId: string | null; empresa: string; plantilla: string; asunto: string; estado: string; motivo: string | null; creadoEn: string }[];
  };
  empresasConMasErrores: { empresaId: string; empresa: string; consultasConError: number; correosFallidos: number }[];
}

export const FUENTES: Record<string, string> = {
  SimitNit: "SIMIT por NIT",
  SimitPlaca: "SIMIT por placa",
  MunicipalNit: "Municipal por NIT",
  MunicipalPlaca: "Municipal por placa",
};

export const TIPOS_ERROR: Record<string, string> = {
  Timeout: "Tiempo de espera agotado",
  HttpNoExitoso: "Respuesta HTTP no exitosa",
  SoapFault: "Error SOAP",
  RespuestaInvalida: "Respuesta inválida",
  Red: "Error de red",
  Autenticacion: "Autenticación rechazada",
  EstadoNoOk: "Estado distinto de OK",
  Excepcion: "Excepción no controlada",
  SinTipo: "Sin clasificar",
};

/** Plantillas de correo en términos de negocio (la clave técnica solo como último recurso). */
const PLANTILLAS: Record<string, string> = {
  "comparendos.evidencia": "Documento del comparendo al conductor",
  "comparendos.dp_ot": "Derecho de petición al organismo",
  "comparendos.cambio_contraventor_ot": "Cambio de contraventor al organismo",
  "comparendos.reporte_semanal": "Reporte semanal",
  "comparendos.reporte_mensual": "Reporte mensual",
  "comparendos.consulta_programada": "Consulta programada",
  "comparendos.resumen_envio_automatico_dp": "Resumen del envío automático de DP",
  "comparendos.alerta_fallo_integracion": "Alerta: falla de integración",
  "comparendos.alerta_fallo_ocr": "Alerta: falla de lectura de documentos",
  "comparendos.alerta_sin_representante": "Alerta: empresa sin representante legal",
  "comparendos.superadmin": "Notificación a administración FLIT",
  "comparendos.usuario": "Notificación a un usuario",
};

export function etiquetaPlantilla(clave: string): string {
  const aviso = /^comparendos\.aviso_?(\d)$/.exec(clave);
  if (aviso) return `Aviso ${aviso[1]} al conductor`;
  return PLANTILLAS[clave] ?? clave;
}
