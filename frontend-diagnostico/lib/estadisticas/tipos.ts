// Tipos de las estadísticas de Diagnóstico (contrato-api.md §1, esquema EstadisticasDto de diagnostico.v1.yaml 1.5.0):
// GET /estadisticas (compañía), /backoffice/estadisticas/global y /backoffice/estadisticas?empresa=. JSON snake_case.
// Sin PII en ninguna profundidad (SP-16): ni placa, VIN, documento ni nombre de usuario.
import type { Fuente } from "@/lib/configuracion";
import type { EstadoSaneamiento } from "@/lib/saneamiento/api";

export type Granularidad = "DIA" | "SEMANA" | "MES";

export interface AmbitoEstadisticas {
  tipo: "EMPRESA" | "GLOBAL";
  empresa_id: string | null;
  empresa_nombre: string | null;
}

export interface ResumenEstadisticas {
  diagnosticos: number;
  diagnosticos_express: number;
  diagnosticos_masivos: number;
  vehiculos: number;
  vehiculos_unicos: number;
  consultas: number;
  consultas_exitosas: number;
  consultas_con_error: number;
  consultas_no_aplica: number;
  consultas_pendientes: number;
  /** exitosas / (exitosas + con_error), 0..1; null si el divisor es 0. */
  tasa_exito: number | null;
  tiempo_promedio_consulta_ms: number | null;
  saneamientos_solicitados: number;
  tramites_creados: number;
}

export interface PuntoSerie {
  /** AAAA-MM-DD de la cubeta (lunes en SEMANA, día 1 en MES). */
  inicio: string;
  diagnosticos: number;
  express: number;
  masivos: number;
  vehiculos: number;
}

export interface EstadisticaFuente {
  fuente: Fuente;
  consultas: number;
  exitosas: number;
  con_error: number;
  no_aplica: number;
  pendientes: number;
  tasa_exito: number | null;
  tiempo_promedio_ms: number | null;
}

export interface TopEmpresa {
  empresa_id: string;
  empresa_nombre: string;
  diagnosticos: number;
  vehiculos: number;
}

export interface EstadisticasDto {
  ambito: AmbitoEstadisticas;
  periodo: { desde: string; hasta: string; granularidad: Granularidad };
  generado_en: string;
  resumen: ResumenEstadisticas;
  serie: PuntoSerie[];
  veredictos: { viables: number; bloqueados: number; sin_dictamen: number };
  tipos_analisis: { simple: number; con_analisis: number };
  fuentes: EstadisticaFuente[];
  saneamientos: Array<{ estado: EstadoSaneamiento; cantidad: number }>;
  derivaciones: { creadas: number; omitidas: number; fallidas: number; en_curso: number };
  /** Solo en la vista global; [] en las de compañía. */
  top_empresas: TopEmpresa[];
}

/** Rango de fechas AAAA-MM-DD (hora Colombia, días completos). */
export interface Rango {
  desde: string;
  hasta: string;
}
