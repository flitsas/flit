/** `GET /reportes/dp` (Reportes 2.0, S3). La tasa y el ahorro son los del Resumen (`KpisAsync`, R1). */
export interface FilaDp {
  clave: string;
  nombre: string;
  /** Radicados en el periodo. */
  enviados: number;
  /** Respuestas recibidas en el periodo: el mismo corte de la tasa del Resumen. */
  favorables: number;
  desfavorables: number;
  /** Radicados en el periodo que siguen esperando respuesta. */
  sinRespuesta: number;
  diasPromedio: number | null;
  /** favorables / (favorables + desfavorables), el mismo corte del Resumen; null sin respuestas («—»). */
  tasaExito: number | null;
}

export interface ReporteDp {
  tasaExito: number | null;
  ahorro: number;
  favorables: number;
  desfavorables: number;
  /** A hoy: todo lo enviado que espera respuesta, como en el Resumen. */
  sinRespuesta: number;
  enviados: number;
  porOrganismo: FilaDp[];
  porInfraccion: FilaDp[];
  cruce: { organismoId: string; organismo: string; infraccion: string; enviados: number; favorables: number; desfavorables: number; tasaExito: number | null }[];
  positivos: { comparendoId: string; numero: string; placa: string | null; radicado: string; organismo: string; fechaRadicacion: string | null; fechaRespuesta: string; valorAlRadicar: number | null }[];
  totalPositivos: number;
  acumulado: { dia: string; enviados: number; respondidos: number }[];
  vencidos: { comparendoId: string; numero: string; placa: string | null; radicado: string; organismo: string; fechaRadicacion: string | null; fechaLimite: string; diasVencido: number }[];
  totalVencidos: number;
  /** Solo el SuperAdmin con «todas las empresas». */
  consolidado: { empresaId: string; empresa: string; organismoId: string; organismo: string; enviados: number; favorables: number; desfavorables: number; sinRespuesta: number; tasaExito: number | null }[] | null;
}

export interface RespuestaDp {
  alcance: { empresa: string | null; nit: string | null; desde: string; hasta: string };
  dp: ReporteDp;
}
