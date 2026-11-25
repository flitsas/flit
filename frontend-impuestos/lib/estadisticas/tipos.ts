// Dashboard de Impuestos (contracts/openapi/impuestos.v1.yaml 1.0.0, GET /api/v1/impuestos/dashboard, schema Dashboard).
// Sin datos personales. Un bloque que llega null (back anterior o fallo parcial) se pinta «Sin datos para este bloque».

/** Periodo AAAA-MM-DD, extremos incluidos (hora de Colombia). */
export interface Rango {
  desde: string;
  hasta: string;
}

/** Contadores de una liquidación (schema Contadores). */
export interface ContadoresLiquidacion {
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

export interface Referencia {
  id: string;
  nombre: string;
}

/** Fila de «Últimos 5 lotes» (schema ResumenLiquidacion). */
export interface ResumenLiquidacion {
  id: string;
  tipo: "INDIVIDUAL" | "MASIVA" | string;
  estado: string;
  totalItems: number;
  contadores: ContadoresLiquidacion;
  creadaEn: string;
  creadaPor: Referencia;
  empresa: Referencia;
  nombreArchivo?: string | null;
  placa?: string | null;
}

export interface DashboardImpuestos {
  periodo: Rango;
  contadores: { total: number; individuales: number; masivas: number; lotesEnProceso: number } | null;
  resultados: {
    exitosas: number;
    conError: number;
    pendientesWebhook: number;
    pendientesConsulta: number;
    datosIncompletos: number;
    departamentoNoHabilitado: number;
    inciertas: number;
    sinResultado: number;
  } | null;
  deuda: { placasConDeuda: number; placasAlDia: number } | null;
  /** Suma de totalAPagar sin los null; el contrato lo declara no nulo, el front tolera null («Sin dato»). */
  montoTotal: number | null;
  liquidacionesSinMonto: number;
  parcial: boolean;
  serie: Array<{ fecha: string; total: number }> | null;
  porDepartamento: Array<{ codigo: string; nombre: string; total: number }> | null;
  ultimosLotes: ResumenLiquidacion[] | null;
  /** Solo Super Admin en la vista global; null en los demás. */
  ranking: Array<{ empresa: Referencia; total: number }> | null;
}
