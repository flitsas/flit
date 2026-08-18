// El modelo de una consulta armada por el usuario, sin dominio propio.
//
// Lo comparten dos módulos: las consultas del organismo de tránsito y las de la empresa gestora.
// Preguntan sobre trámites distintos y con catálogos distintos, pero la FORMA de la pregunta es la
// misma, y tiene que seguir siéndolo: un enlace compartible, una consulta guardada y el aviso de
// cobertura son el mismo contrato en los dos lados.
//
// El espejo de `Flit.Queries.Domain` en el backend. El catálogo de campos NO está escrito aquí ni
// allá en el frontend: lo sirve el servidor, y por eso agregar un campo consultable no requiere
// desplegar frontend.

export type QueryFieldKind = "texto" | "opcion" | "booleano";

export type QueryOperator =
  | "es_alguno"
  | "no_es_ninguno"
  | "contiene"
  | "esta_vacio"
  | "no_esta_vacio";

export interface QueryFieldOption {
  value: string;
  label: string;
}

export interface QueryField {
  id: string;
  label: string;
  kind: QueryFieldKind;
  group: string;
  operators: QueryOperator[];
  options: QueryFieldOption[];
  hint: string | null;
  /** Si tiene sentido pegar una lista de valores (placas, VIN, radicados). */
  admiteLista: boolean;
}

/** Etiquetas de los operadores tal y como se leen dentro de un chip. */
export const OPERATOR_LABEL: Record<QueryOperator, string> = {
  es_alguno: "es",
  no_es_ninguno: "no es",
  contiene: "contiene",
  esta_vacio: "está vacío",
  no_esta_vacio: "tiene dato",
};

export const UNARY_OPERATORS: QueryOperator[] = ["esta_vacio", "no_esta_vacio"];

export interface QueryCondition {
  fieldId: string;
  operator: QueryOperator;
  values: string[];
}

export type QueryRangePreset =
  | "hoy"
  | "ultimos_7"
  | "ultimos_30"
  | "ultimos_90"
  | "mes_actual"
  | "mes_anterior"
  | "anio_actual"
  | "personalizado";

/**
 * El rango se guarda RELATIVO («últimos 30 días»), no con extremos fijos, y lo resuelve el servidor
 * contra el día de Bogotá en cada ejecución. Una consulta guardada con «1 al 31 de agosto» mentiría
 * en septiembre.
 */
export interface QueryDateFilter {
  campo: string;
  preset: QueryRangePreset;
  from?: string | null;
  to?: string | null;
}

export interface QueryDefinition {
  fechas: QueryDateFilter;
  condiciones: QueryCondition[];
  columnas: string[];
  sortBy?: string | null;
  descending?: boolean;
}

export const RANGE_PRESETS: { value: QueryRangePreset; label: string }[] = [
  { value: "hoy", label: "Hoy" },
  { value: "ultimos_7", label: "Últimos 7 días" },
  { value: "ultimos_30", label: "Últimos 30 días" },
  { value: "ultimos_90", label: "Últimos 90 días" },
  { value: "mes_actual", label: "Mes actual" },
  { value: "mes_anterior", label: "Mes anterior" },
  { value: "anio_actual", label: "Año actual" },
  { value: "personalizado", label: "Rango propio" },
];

export type QueryCoverageResult = "encontrado" | "excluido" | "no_existe";

/**
 * Qué pasó con cada valor que el usuario pidió por nombre. Sin esto, un resultado con menos filas
 * de las esperadas se lee como «se perdió un dato».
 */
export interface QueryCoverageItem {
  campo: string;
  valor: string;
  resultado: QueryCoverageResult;
  motivoCampo: string | null;
  motivo: string | null;
}

export interface SavedQuery {
  id: string;
  nombre: string;
  descripcion: string | null;
  /** Las de fábrica no se editan ni se borran: guardarlas las duplica. */
  deFabrica: boolean;
  definition: QueryDefinition;
  createdAt: string;
  updatedAt: string | null;
}

/** El resultado, con las filas del módulo que sea. */
export interface QueryResult<TRow> {
  total: number;
  page: number;
  pageSize: number;
  desde: string;
  hasta: string;
  totalPeriodoAnterior: number;
  filas: TRow[];
  cobertura: QueryCoverageItem[];
}

export const QUERY_PAGE_SIZE = 50;
export const QUERY_MAX_PAGE_SIZE = 200;

export interface SaveQueryInput {
  id?: string;
  nombre: string;
  descripcion?: string | null;
  definition: QueryDefinition;
}

/**
 * Lo que la consola necesita saber de un módulo para funcionar: cómo se llaman sus fechas y cómo se
 * habla con su API. Todo lo demás —campos, operadores, cobertura— sale del catálogo del servidor.
 */
export interface QuerySource<TRow> {
  /** Prefijo de los `data-testid`, para que las dos consolas sean distinguibles en las pruebas. */
  testIdPrefix: string;
  /** Fechas sobre las que se puede aplicar el rango, en orden de oferta. */
  dateFields: { value: string; label: string }[];
  /** La fecha con la que se abre una consulta nueva. */
  defaultDateField: string;
  /** Clave de `localStorage` donde se recuerda la selección de columnas. */
  columnsStorageKey: string;
  /** Prefijo del nombre del archivo exportado. */
  exportPrefix: string;
  /** Cómo se nombra una fila en singular y plural («trámite» / «trámites»). */
  rowNoun: [string, string];
  fetchFields: (signal?: AbortSignal) => Promise<QueryField[]>;
  run: (
    definition: QueryDefinition,
    options: { page?: number; pageSize?: number; signal?: AbortSignal },
  ) => Promise<QueryResult<TRow>>;
  fetchSaved: (signal?: AbortSignal) => Promise<SavedQuery[]>;
  save: (input: SaveQueryInput) => Promise<SavedQuery>;
  remove: (id: string) => Promise<void>;
  /**
   * Reportes 2.0 (HU-D, segunda ola) — "Programar este informe": solo Consultas de empresa y
   * SuperAdmin lo ofrecen (Consultas del organismo no lo pasa, así que el botón no aparece ahí).
   */
  onSchedule?: (query: SavedQuery) => void;
}
