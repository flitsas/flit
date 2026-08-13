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
  /**
   * Agrupación en el desplegable "Columnas". NO altera el orden de la tabla, que lo sigue dando
   * este arreglo: separa las columnas del listado base de los desgloses adicionales, que si no
   * quedaban intercalados entre las base y la lista se leía revuelta.
   */
  group?: string;
}

const GRUPO_BASE = 'Listado';
const GRUPO_DESGLOSE = 'Desglose adicional';

export const TRAMITES_COLUMNS: readonly TramitesColumnDef[] = [
  // Celdas COMPUESTAS (paridad con el diseño): `radicado` apila las fechas, `placa` apila el
  // vehículo y `tramite` apila el chip de estado. Cada añadido es CONDICIONAL — solo se apila si
  // la columna dedicada correspondiente está oculta (ver `composeExtras` en TramitesTable), así
  // activar la columna dedicada desde el selector nunca duplica el dato. Por eso estas tres son
  // más anchas que su equivalente de una sola línea.
  { key: 'radicado', label: 'Radicado', width: '1.4fr', minPx: 190, group: GRUPO_BASE },
  { key: 'vin', label: 'VIN', width: '1.1fr', minPx: 130, sortable: true, group: GRUPO_BASE },
  { key: 'placa', label: 'Placa', width: '1.1fr', minPx: 130, sortable: true, group: GRUPO_BASE },
  // Firma del actor va DENTRO de la misma celda (nombre + chip), no como columna aparte: por eso
  // estas dos son algo más anchas que el resto.
  { key: 'propietario', label: 'Propietario / vendedor', width: '1.4fr', minPx: 170, group: GRUPO_BASE },
  { key: 'comprador', label: 'Comprador', width: '1.4fr', minPx: 170, sortable: true, group: GRUPO_BASE },
  // UNA sola columna de firmas para las dos partes, como en el diseño: dentro lleva una línea por
  // parte (vendedor y comprador) porque la acreditación es POR PARTE, no del trámite. En matrícula
  // inicial solo hay comprador, así que la celda muestra una sola línea.
  { key: 'firmado', label: 'Firmas', width: '1.2fr', minPx: 150, group: GRUPO_BASE },
  { key: 'tramite', label: 'Trámite / Estado', width: '1.3fr', minPx: 160, group: GRUPO_BASE },
  // Sin truncar: el nombre del organismo es la mitad del valor de la columna ("SECRETARIA
  // DISTRITAL DE MOVILIDAD DE BOGOTA" cortado a "SECRETARIA DISTRITAL DE…" no distingue nada).
  // Envuelve en varias líneas, por eso pide más ancho que una columna de una sola línea.
  { key: 'secretaria', label: 'Secretaría', width: '1.6fr', minPx: 190, group: GRUPO_BASE },
  { key: 'gestor', label: 'Gestor', width: '1.3fr', minPx: 160, sortable: true, group: GRUPO_BASE },
  { key: 'fuente', label: 'Fuente', width: '0.9fr', minPx: 110, group: GRUPO_BASE },
  // Desgloses: su dato ya viaja apilado en una celda del listado (vehículo bajo Placa, estado y
  // paso bajo Trámite, fechas bajo Radicado). Activarlos lo MUEVE a su propia columna.
  { key: 'vehiculo', label: 'Vehículo', width: '1.1fr', minPx: 130, group: GRUPO_DESGLOSE },
  { key: 'estado', label: 'Estado', width: '1.2fr', minPx: 150, group: GRUPO_DESGLOSE },
  { key: 'paso', label: 'Paso', width: '1fr', minPx: 110, group: GRUPO_DESGLOSE },
  { key: 'fechaCreacion', label: 'Fecha de creación', width: '0.9fr', minPx: 110, sortable: true, group: GRUPO_DESGLOSE },
  { key: 'fechaActualizacion', label: 'Fecha de actualización', width: '0.9fr', minPx: 120, sortable: true, group: GRUPO_DESGLOSE },
] as const;

/**
 * Catálogo de claves conocidas HOY. Se persiste junto a la preferencia del usuario (`known`) para
 * distinguir una columna que ocultó a propósito de una que todavía no existía cuando guardó: sin
 * esto, cada columna nueva nace invisible para quien ya tenía preferencia y parece un dato que falta.
 */
export const TRAMITES_COLUMN_KEYS: readonly string[] = TRAMITES_COLUMNS.map((c) => c.key);

/**
 * Claves añadidas al catálogo DESPUÉS de que ya hubiera preferencias guardadas sin registro de
 * catálogo (`known`). Solo estas se incorporan a una preferencia antigua: el resto de la selección
 * del usuario se respeta, porque haber ocultado una columna que ya existía sí fue decisión suya.
 *
 * No crece indefinidamente: en cuanto el usuario guarda una vez, su preferencia pasa a llevar
 * `known` y la deducción se vuelve exacta. Esta lista solo cubre el salto desde el formato viejo.
 */
export const TRAMITES_COLUMNS_ADDED_SINCE_LEGACY: readonly string[] = ['firmado'];

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

/**
 * Columnas visibles por defecto: el set del diseño de la pantalla principal de trámites.
 *
 * `vehiculo`, `paso`, `estado`, `fechaCreacion` y `fechaActualizacion` NO desaparecieron: siguen
 * en TRAMITES_COLUMNS y el usuario puede activarlas con "Columnas". Quedan fuera del default
 * porque su dato ya viaja apilado dentro de `placa`, `tramite` y `radicado` — activarlas mueve el
 * dato a su propia columna en vez de duplicarlo.
 */
export const DEFAULT_TRAMITES_VISIBLE_COLUMNS: readonly string[] = [
  'radicado',
  'vin',
  'placa',
  'propietario',
  'comprador',
  'firmado',
  'tramite',
  'secretaria',
  'gestor',
  'fuente',
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
