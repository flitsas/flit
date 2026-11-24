// Consultas personalizadas de Comparendos (Reportes 2.0, pestaña Consultas).
//
// Copia ADAPTADA del modelo de la consola de Trámites (`frontend/lib/api/queries.ts`), no un paquete compartido: la
// FORMA de la pregunta es la misma que en Trámites (mismo motor, `Flit.Queries.Domain`), y lo propio de Comparendos es
// la fila (`ComparendoQueryRow`, contrato §4) y las rutas. El catálogo de campos lo sirve el servidor: un campo
// consultable nuevo aparece en pantalla sin desplegar el frontend.

import { apiFetch, ApiError } from "@/lib/api";

export type QueryFieldKind = "texto" | "opcion" | "booleano";

export type QueryOperator = "es_alguno" | "no_es_ninguno" | "contiene" | "esta_vacio" | "no_esta_vacio";

export interface QueryFieldOption {
  value: string;
  label: string;
  /** Encabezado bajo el que se agrupa la opción, si el servidor lo manda. */
  group?: string | null;
}

export interface QueryField {
  id: string;
  label: string;
  kind: QueryFieldKind;
  group: string;
  operators: QueryOperator[];
  options: QueryFieldOption[];
  hint: string | null;
  /** Si tiene sentido pegar una lista de valores (números de comparendo, placas, radicados). */
  admiteLista: boolean;
}

/** Etiquetas de los operadores tal y como se leen dentro de una ficha. */
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
  | "ultimos_365"
  | "proximos_30"
  | "proximos_90"
  | "personalizado";

/** El rango se guarda RELATIVO («últimos 30 días») y lo resuelve el servidor contra el día de Bogotá en cada ejecución. */
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

/**
 * `futuro`: el rango mira hacia adelante desde hoy. Solo tiene sentido sobre fechas que todavía no han pasado (un
 * plazo, una caducidad), y la consola lo ofrece solo con las de `QuerySource.camposFuturos`.
 */
export const RANGE_PRESETS: { value: QueryRangePreset; label: string; futuro?: boolean }[] = [
  { value: "hoy", label: "Hoy" },
  { value: "ultimos_7", label: "Últimos 7 días" },
  { value: "ultimos_30", label: "Últimos 30 días" },
  { value: "ultimos_90", label: "Últimos 90 días" },
  { value: "mes_actual", label: "Mes actual" },
  { value: "mes_anterior", label: "Mes anterior" },
  { value: "anio_actual", label: "Año actual" },
  { value: "ultimos_365", label: "Último año" },
  { value: "proximos_30", label: "Próximos 30 días", futuro: true },
  { value: "proximos_90", label: "Próximos 90 días", futuro: true },
  { value: "personalizado", label: "Rango propio" },
];

export type QueryCoverageResult = "encontrado" | "excluido" | "no_existe";

/** Qué pasó con cada valor que el usuario pidió por nombre (número, placa, radicado). */
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
  /** Consultas del sistema (de fábrica): no se editan ni se borran; guardarlas crea una copia propia. */
  deFabrica: boolean;
  definition: QueryDefinition;
  createdAt: string;
  updatedAt: string | null;
}

export interface QueryResult<TRow> {
  total: number;
  page: number;
  pageSize: number;
  desde: string;
  hasta: string;
  /** `null` en `page > 1` y cuando el periodo anterior no cabe en el tope: entonces no se compara. */
  totalPeriodoAnterior: number | null;
  filas: TRow[];
  cobertura: QueryCoverageItem[];
}

export const QUERY_PAGE_SIZE = 50;
export const QUERY_MAX_PAGE_SIZE = 200;
/** «Filas por página» de la consola: hasta el máximo del servidor (excepción a los 10/20/30/50 del estándar). */
export const QUERY_PAGE_SIZES = [20, 50, 100, 200] as const;

export interface SaveQueryInput {
  id?: string;
  nombre: string;
  descripcion?: string | null;
  definition: QueryDefinition;
}

/** Lo que la consola necesita del módulo: cómo se llaman sus fechas y cómo se habla con el servidor. */
export interface QuerySource<TRow> {
  testIdPrefix: string;
  dateFields: { value: string; label: string }[];
  defaultDateField: string;
  /** Fechas que miran hacia adelante: las únicas con las que se ofrecen los presets «Próximos …». */
  camposFuturos?: string[];
  columnsStorageKey: string;
  exportPrefix: string;
  rowNoun: [string, string];
  fetchFields: (signal?: AbortSignal) => Promise<QueryField[]>;
  run: (definition: QueryDefinition, options: { page?: number; pageSize?: number; signal?: AbortSignal }) => Promise<QueryResult<TRow>>;
  fetchSaved: (signal?: AbortSignal) => Promise<SavedQuery[]>;
  save: (input: SaveQueryInput) => Promise<SavedQuery>;
  remove: (id: string) => Promise<void>;
  /** Ronda 2: «Programar envío». Sin esto el botón no aparece. */
  onSchedule?: (query: SavedQuery) => void;
}

// ── Comparendos ────────────────────────────────────────────────────────────────

/**
 * Fila de la consulta (contrato §4). Fechas `yyyy-MM-dd` en hora de Colombia; `null` cuando no aplica. Los estados
 * llegan como el VALOR de la opción (`EnTiempo`, `Aviso2`); la etiqueta sale de `/fields`.
 */
export interface ComparendoQueryRow {
  id: string;
  numero: string;
  placa: string | null;
  nitMonitoreado: string | null;
  titular: string | null;
  /** Siempre vienen; con dato solo para el SuperAdmin con «todas las empresas». */
  empresaId: string | null;
  empresaNombre: string | null;
  codigoInfraccion: string | null;
  descripcionInfraccion: string | null;
  esFotomulta: boolean | null;
  tipoVehiculo: string | null;
  servicioVehiculo: string | null;
  organismoId: string | null;
  organismoNombre: string | null;
  departamento: string | null;
  municipio: string | null;
  fechaImposicion: string | null;
  fechaNotificacion: string | null;
  fechaIngreso: string | null;
  fechaLimiteGestion: string | null;
  fechaFinDescuento50: string | null;
  fechaFinDescuento25: string | null;
  fechaCaducidad: string | null;
  fechaPrescripcion: string | null;
  valorCapital: number | null;
  valorAdicional: number | null;
  valorTotal: number | null;
  activo: boolean | null;
  estadoLegal: string | null;
  ventanaDescuento: string | null;
  enCobroCoactivo: boolean | null;
  estadoGestion: string | null;
  gestionado: boolean | null;
  pagado: boolean | null;
  tieneDp: boolean | null;
  dpEstado: string | null;
  dpRadicado: string | null;
  dpTipologia: string | null;
  dpEnvioAutomatico: boolean | null;
  dpFechaRadicacion: string | null;
  dpSentido: string | null;
  dpFechaRespuesta: string | null;
  estadoNotificacionConductor: string | null;
  notificadoFisicamente: boolean | null;
  estadoCambioContraventor: string | null;
  estadoConsultaContraventor: string | null;
  pagoTipo: string | null;
  pagoValor: number | null;
  pagoFecha: string | null;
}

/** Fechas sobre las que se aplica el rango (catálogo §5, `DateFields`). */
export const COMPARENDO_DATE_FIELDS: { value: string; label: string }[] = [
  { value: "imposicion", label: "Imposición" },
  { value: "notificacion", label: "Notificación" },
  { value: "ingreso", label: "Ingreso a la plataforma" },
  { value: "dp_radicacion", label: "Radicación del DP" },
  { value: "dp_respuesta", label: "Respuesta del DP" },
  { value: "pago", label: "Pago" },
  { value: "limite_gestion", label: "Límite de gestión" },
  { value: "caducidad", label: "Caducidad" },
  { value: "prescripcion", label: "Prescripción" },
];

/** Las fechas de un comparendo que todavía pueden estar por venir. */
export const COMPARENDO_CAMPOS_FUTUROS = ["limite_gestion", "caducidad", "prescripcion"];

const BASE = "/reportes/consultas";

/** Error que el servidor marca como «demasiado amplia»: se muestra como aviso, con su mensaje, y no como avería. */
export const CODIGO_DEMASIADO_AMPLIA = "CONSULTA_DEMASIADO_AMPLIA";

/**
 * Código del error de negocio. El servicio responde `{ code, error }` (`Plataforma/Errores.cs`), y `ApiError` ya los
 * lee; el contrato §4 los nombra `{ codigo, mensaje }`, así que se aceptan las dos formas.
 */
export function codigoDeError(e: unknown): string | undefined {
  if (!(e instanceof ApiError)) return undefined;
  if (e.code) return e.code;
  const data = e.data as { codigo?: unknown } | null | undefined;
  return typeof data?.codigo === "string" ? data.codigo : undefined;
}

export function mensajeDeError(e: unknown, porDefecto: string): string {
  if (e instanceof ApiError) {
    const data = e.data as { mensaje?: unknown } | null | undefined;
    if (typeof data?.mensaje === "string" && data.mensaje) return data.mensaje;
    return e.message || porDefecto;
  }
  return e instanceof Error && e.message ? e.message : porDefecto;
}

export function esDemasiadoAmplia(e: unknown): boolean {
  return e instanceof ApiError && e.status === 422 && codigoDeError(e) === CODIGO_DEMASIADO_AMPLIA;
}

/** Cliente de la pestaña. `empresa` solo lo manda el SuperAdmin (vacío = todas las empresas). */
export function fuenteComparendos(empresa: string | null, todas = false): QuerySource<ComparendoQueryRow> {
  return {
    testIdPrefix: "cmp-query",
    dateFields: COMPARENDO_DATE_FIELDS,
    defaultDateField: "imposicion",
    camposFuturos: COMPARENDO_CAMPOS_FUTUROS,
    // Aparte para «todas las empresas» del SuperAdmin: la columna Empresa no debería quedarse puesta al mirar una sola.
    columnsStorageKey: todas ? "flit-cmp-consultas-columnas-todas" : "flit-cmp-consultas-columnas",
    exportPrefix: "consulta-comparendos",
    rowNoun: ["comparendo", "comparendos"],
    fetchFields: (signal) => apiFetch<QueryField[]>(`${BASE}/fields`, { signal, query: { empresa } }),
    run: (definition, { page, pageSize, signal }) =>
      apiFetch<QueryResult<ComparendoQueryRow>>(`${BASE}/run`, {
        method: "POST",
        body: definition,
        signal,
        query: { empresa, page: page ?? 1, pageSize: pageSize ?? QUERY_PAGE_SIZE },
      }),
    fetchSaved: (signal) => apiFetch<SavedQuery[]>(`${BASE}/saved`, { signal, query: { empresa } }),
    save: (input) => apiFetch<SavedQuery>(`${BASE}/saved`, { method: "POST", body: input, query: { empresa } }),
    remove: (id) => apiFetch<void>(`${BASE}/saved/${encodeURIComponent(id)}`, { method: "DELETE", query: { empresa } }),
  };
}

// ── Programación del envío por correo (ronda 2, contrato §4 `/programaciones`) ─────────────────────────────────────

export type Periodicidad = "semanal" | "mensual";

/** Envío programado de una consulta guardada. `ultimoResultado` ya viene en lenguaje de negocio. */
export interface ConsultaProgramada {
  id: string;
  consultaId: string;
  periodicidad: Periodicidad;
  /** Día de la semana como `DayOfWeek` de .NET: 0 = domingo … 6 = sábado. Solo en semanal. */
  diaSemana: number | null;
  /** 1–28. Solo en mensual. */
  diaMes: number | null;
  /** Hora de Colombia, «HH:mm» (el servidor puede mandar «HH:mm:ss»). */
  hora: string;
  destinatarios: string[];
  activa: boolean;
  ultimaEjecucion: string | null;
  proximaEjecucion: string | null;
  ultimoResultado: string | null;
}

export interface GuardarProgramacion {
  id?: string;
  consultaId: string;
  periodicidad: Periodicidad;
  diaSemana?: number | null;
  diaMes?: number | null;
  hora: string;
  destinatarios: string[];
  activa: boolean;
}

const PROGRAMACIONES = `${BASE}/programaciones`;

export function listarProgramaciones(empresa: string | null, signal?: AbortSignal): Promise<ConsultaProgramada[]> {
  return apiFetch<ConsultaProgramada[]>(PROGRAMACIONES, { signal, query: { empresa } });
}

export function guardarProgramacion(entrada: GuardarProgramacion, empresa: string | null): Promise<ConsultaProgramada> {
  return apiFetch<ConsultaProgramada>(PROGRAMACIONES, { method: "POST", body: entrada, query: { empresa } });
}

export function borrarProgramacion(id: string, empresa: string | null): Promise<void> {
  return apiFetch<void>(`${PROGRAMACIONES}/${encodeURIComponent(id)}`, { method: "DELETE", query: { empresa } });
}
