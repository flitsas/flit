/**
 * Definición de las columnas configurables de la tabla "Trámites en curso" del gestor
 * (selector de columnas, preferencia `tramites.columns` — ver lib/api/ui-preferences.ts).
 *
 * Única fuente de verdad de: (a) las claves que viajan en la preferencia persistida, (b) el
 * ancho de cada columna en el grid CSS de TramitesTable y (c) la etiqueta que ve el usuario en
 * el selector. Las columnas "Selección" (checkbox ICT) y "Acciones" son estructurales — no son
 * parte de la preferencia; Selección solo se reserva cuando hay borradores ICT en la página.
 */
export interface TramitesColumnDef {
  key: string;
  /** Etiqueta plana: nombre del checkbox del selector y, salvo excepción, texto de la cabecera. */
  label: string;
  /** Fracción del grid (`gridTemplateColumns`), mismo criterio que el resto de la tabla. */
  width: string;
  /** Ancho mínimo legible en píxeles, insumo del `min-width` del contenedor con scroll horizontal. */
  minPx: number;
  /** Si la cabecera admite clic para ordenar vía API (`sortBy` / `sortDir`). */
  sortable?: boolean;
}

export const TRAMITES_COLUMNS: readonly TramitesColumnDef[] = [
  { key: 'radicado', label: 'Radicado', width: '1.2fr', minPx: 170 },
  { key: 'vin', label: 'VIN', width: '1.1fr', minPx: 130, sortable: true },
  { key: 'placa', label: 'Placa', width: '0.9fr', minPx: 110, sortable: true },
  { key: 'tramite', label: 'Trámite / Modalidad', width: '0.9fr', minPx: 110 },
  { key: 'vehiculo', label: 'Vehículo', width: '1.1fr', minPx: 130 },
  // Firma del actor va DENTRO de la misma celda (nombre + chip), no como columna aparte: por eso
  // estas dos son algo más anchas que el resto.
  { key: 'propietario', label: 'Propietario / vendedor', width: '1.4fr', minPx: 170 },
  { key: 'comprador', label: 'Comprador', width: '1.4fr', minPx: 170, sortable: true },
  { key: 'paso', label: 'Paso', width: '1fr', minPx: 110 },
  { key: 'estado', label: 'Estado', width: '1.2fr', minPx: 150 },
  { key: 'fechaCreacion', label: 'Fecha de creación', width: '0.9fr', minPx: 110, sortable: true },
  { key: 'fechaActualizacion', label: 'Fecha de actualización', width: '0.9fr', minPx: 120, sortable: true },
  { key: 'secretaria', label: 'Secretaría', width: '1.1fr', minPx: 130 },
  { key: 'gestor', label: 'Gestor', width: '1.3fr', minPx: 160, sortable: true },
  { key: 'fuente', label: 'Fuente', width: '0.9fr', minPx: 110 },
] as const;

/** Clave de UI → `sortBy` del API de listado de trámites. */
export function tramitesColumnToSortBy(columnKey: string): string {
  switch (columnKey) {
    case 'fechaCreacion':
      return 'createdAt';
    case 'fechaActualizacion':
      return 'updatedAt';
    case 'vin':
    case 'placa':
    case 'comprador':
    case 'gestor':
      return columnKey;
    default:
      return columnKey;
  }
}

/** Columnas visibles por defecto: lo esencial para operar sin scroll lateral.
 * El resto (VIN, vehículo, gestor, fuente, etc.) el usuario las activa con "Columnas". */
export const DEFAULT_TRAMITES_VISIBLE_COLUMNS: readonly string[] = [
  'radicado',
  'placa',
  'tramite',
  'propietario',
  'comprador',
  'paso',
  'estado',
  'fechaCreacion',
  'secretaria',
] as const;

const SELECT_COL_WIDTH = '2.25rem';
const SELECT_COL_MIN_PX = 36;
const ACTIONS_COL_WIDTH = '1.2fr';
const ACTIONS_COL_MIN_PX = 140;

export interface TramitesGridLayout {
  /** Listo para `style.gridTemplateColumns`: columnas visibles + Acciones (+ Selección si aplica). */
  gridTemplateColumns: string;
  /** Ancho mínimo (px) sugerido para el contenedor con `overflow-x-auto`. */
  minWidthPx: number;
  /** Si la grilla reserva la primera pista para el checkbox ICT. */
  includeSelectColumn: boolean;
}

export interface BuildTramitesGridLayoutOptions {
  /**
   * Reserva la pista del checkbox de selección masiva ICT. Solo debe ser true cuando hay al
   * menos un borrador ICT en el listado; si no, deja un hueco vacío al inicio de cada fila.
   */
  includeSelectColumn?: boolean;
}

/**
 * Construye el `gridTemplateColumns` (y el ancho mínimo) a partir de las claves visibles. La
 * cabecera y cada fila de TramitesTable deben invocar ESTA MISMA función con el mismo arreglo
 * `visibleKeys`: al derivar ambas del mismo cálculo quedan alineadas por construcción sin
 * importar cuántas columnas se oculten — evita el desalineamiento cabecera/filas típico de un
 * grid con columnas condicionales calculadas dos veces por separado.
 *
 * Si `visibleKeys` no deja ninguna columna conocida (preferencia corrupta o vacía), se cae a
 * TODAS las columnas: nunca se construye un grid vacío que dejaría la tabla inutilizable.
 */
export function buildTramitesGridLayout(
  visibleKeys: readonly string[],
  options: BuildTramitesGridLayoutOptions = {},
): TramitesGridLayout {
  const includeSelectColumn = options.includeSelectColumn === true;
  const matched = TRAMITES_COLUMNS.filter((c) => visibleKeys.includes(c.key));
  const columns = matched.length > 0 ? matched : TRAMITES_COLUMNS;
  return {
    includeSelectColumn,
    gridTemplateColumns: [
      ...(includeSelectColumn ? [SELECT_COL_WIDTH] : []),
      ...columns.map((c) => c.width),
      ACTIONS_COL_WIDTH,
    ].join(' '),
    minWidthPx:
      (includeSelectColumn ? SELECT_COL_MIN_PX : 0) +
      columns.reduce((sum, c) => sum + c.minPx, 0) +
      ACTIONS_COL_MIN_PX,
  };
}
