/**
 * Definición de las columnas configurables de la tabla "Trámites en curso" del gestor
 * (selector de columnas, preferencia `tramites.columns` — ver lib/api/ui-preferences.ts).
 *
 * Única fuente de verdad de: (a) las claves que viajan en la preferencia persistida, (b) el
 * ancho de cada columna en la tabla y (c) la etiqueta que ve el usuario en el selector. Las
 * columnas "Selección" (checkbox ICT) y "Acciones" son estructurales — no son parte de la
 * preferencia; Selección solo se reserva cuando hay borradores ICT en la página.
 */
export interface TramitesColumnDef {
  key: string;
  /** Etiqueta plana: nombre del checkbox del selector y, salvo excepción, texto de la cabecera. */
  label: string;
  /**
   * Ancho mínimo legible, en píxeles. Cumple DOS papeles, y por eso es el único número de ancho
   * que declara una columna:
   *
   * - es el piso de la columna, y la suma de los pisos es el `minWidthPx` a partir del cual la
   *   tabla scrollea en horizontal en vez de seguir comprimiendo;
   * - en una columna FLEXIBLE es además su PESO de crecimiento (`minPx / 100` fr). Que el peso
   *   derive del piso no es cosmético: garantiza que, mientras la tabla mida al menos
   *   `minWidthPx`, ninguna columna quede por debajo de su mínimo. Con pesos escogidos a mano eso
   *   no se sostenía — una columna con peso bajo y piso alto se quedaba corta mucho antes de que
   *   la tabla llegara a su mínimo total;
   * - en una columna FIJA (`fixed`) es su ancho EXACTO a cualquier ancho de tabla.
   */
  minPx: number;
  /**
   * Columna de ancho FIJO: su contenido es atómico y no gana nada con más espacio (un VIN, una
   * fecha, "Integración", el menú de acciones). No participa del reparto del ancho sobrante, así
   * que TODO lo que sobra va a las columnas de texto —nombres, secretaría, trámite—, que son las
   * que sí truncan. Antes crecían todas por igual y el aire terminaba inflando la columna de VIN.
   */
  fixed?: boolean;
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
  // Celdas COMPUESTAS (paridad con el diseño): `radicado` apila las fechas, `placa` apila el VIN y
  // la marca/modelo, y `tramite` apila el chip de estado. Cada añadido es CONDICIONAL — solo se
  // apila si la columna dedicada correspondiente está oculta (ver `shows` en TramitesTable), así
  // activar la columna dedicada desde el selector nunca duplica el dato. Por eso estas tres piden
  // más piso que una celda de una sola línea.
  //
  // El piso lo manda la línea más larga que apila, "Actualización: 2026/08/27": ~158px de texto
  // + los 32px de padding del `<td>`. Con 190px se cortaba a "Actualización: 2026/08…" — una
  // fecha a medias no dice nada, y a diferencia de un nombre no tiene dónde partirse bien.
  { key: 'radicado', label: 'Radicado', minPx: 210, group: GRUPO_BASE },
  // Vehículo = placa + VIN + marca/línea en UNA celda. Los tres identifican el mismo objeto y el
  // gestor los lee juntos; repartidos en tres columnas, la placa quedaba a dos columnas del VIN y
  // había que barrer la fila a lo ancho para reconocer un vehículo.
  //
  // El piso lo manda el VIN, que es lo más ancho que entra: un token de 17 caracteres SIN espacios
  // —no puede envolver como un nombre, o se lee entero o truncado—. JetBrains Mono avanza 0.6em,
  // así que a 12px son 17 × 7.2 ≈ 123px, más los 32px de padding del `<td>` ≈ 155px. Los 180 dejan
  // margen para que la línea de marca/modelo no envuelva a la primera de cambio.
  //
  // La CLAVE sigue siendo `placa`: es lo que viaja en la preferencia guardada de cada usuario.
  // Ordena por placa; para ordenar por VIN está su columna de desglose.
  { key: 'placa', label: 'Vehículo', minPx: 180, sortable: true, group: GRUPO_BASE },
  // La celda solo trae `vendedorNombre`, así que el rótulo es "Vendedor" a secas: "Propietario /
  // vendedor" prometía un propietario inscrito que esta columna nunca ha pintado. La CLAVE sigue
  // siendo `propietario` — es lo que viaja en la preferencia guardada de cada usuario y
  // renombrarla dejaría fuera de sitio a todas las preferencias ya persistidas.
  // Cada actor lleva DENTRO su propia acreditación (identidad validada o firma del baúl): la
  // columna "Firmas" que las agrupaba ya no existe. Estaban separadas y el gestor leía "Firmado"
  // sin saber de quién sin cruzar la vista a otra columna; juntas, la cabecera de la columna ya
  // dice de quién es. El piso cubre el peor caso de las DOS líneas: el nombre completo arriba y
  // el valor más largo ("Sin registrar", ~82px) debajo, más los 32px de padding del `<td>`.
  { key: 'propietario', label: 'Vendedor', minPx: 170, group: GRUPO_BASE },
  { key: 'comprador', label: 'Comprador', minPx: 170, sortable: true, group: GRUPO_BASE },
  { key: 'tramite', label: 'Trámite / Estado', minPx: 160, group: GRUPO_BASE },
  // Sin truncar: el nombre del organismo es la mitad del valor de la columna ("SECRETARIA
  // DISTRITAL DE MOVILIDAD DE BOGOTA" cortado a "SECRETARIA DISTRITAL DE…" no distingue nada).
  // Envuelve en varias líneas, así que es de las que mejor aprovecha el ancho sobrante.
  { key: 'secretaria', label: 'Secretaría', minPx: 190, group: GRUPO_BASE },
  { key: 'gestor', label: 'Gestor', minPx: 160, sortable: true, group: GRUPO_BASE },
  // Fija: tres etiquetas conocidas y cortas ("Dashboard", "Integración", "Migrado").
  { key: 'fuente', label: 'Fuente', minPx: 120, fixed: true, group: GRUPO_BASE },
  // Desgloses: su dato ya viaja apilado en una celda del listado (VIN y marca/modelo bajo
  // Vehículo, estado y paso bajo Trámite, fechas bajo Radicado). Activarlos lo MUEVE a su propia
  // columna: el dato sale de la celda compuesta, nunca aparece dos veces.
  //
  // El rótulo del desglose del vehículo NO puede ser "Vehículo" —ya lo lleva la columna fundida— y
  // "Marca / modelo" es además lo que de verdad pinta: marca + línea.
  { key: 'vin', label: 'VIN', minPx: 168, fixed: true, sortable: true, group: GRUPO_DESGLOSE },
  { key: 'vehiculo', label: 'Marca / modelo', minPx: 140, group: GRUPO_DESGLOSE },
  { key: 'estado', label: 'Estado', minPx: 150, group: GRUPO_DESGLOSE },
  // Fijas: "3/5" con el nombre del paso, y dos fechas de formato constante.
  { key: 'paso', label: 'Paso', minPx: 130, fixed: true, group: GRUPO_DESGLOSE },
  { key: 'fechaCreacion', label: 'Fecha de creación', minPx: 130, fixed: true, sortable: true, group: GRUPO_DESGLOSE },
  { key: 'fechaActualizacion', label: 'Fecha de actualización', minPx: 140, fixed: true, sortable: true, group: GRUPO_DESGLOSE },
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
 * Hoy está VACÍA: la única que la ocupaba era `firmado`, y esa columna dejó de existir cuando la
 * acreditación se metió dentro de la celda de cada actor. Se conserva el mecanismo porque en
 * cuanto el usuario guarda una vez su preferencia pasa a llevar `known` y la deducción se vuelve
 * exacta; esta lista solo cubre el salto desde el formato viejo.
 */
export const TRAMITES_COLUMNS_ADDED_SINCE_LEGACY: readonly string[] = [];

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
 * Columnas visibles por defecto: con lo que arranca un gestor que nunca ha tocado "Columnas".
 *
 * Es solo el punto de partida — en cuanto el gestor cambia la selección, su preferencia manda y
 * este arreglo deja de aplicarle (ver `useUiPreferences` con scope `tramites.columns`).
 *
 * NINGUNA de las que faltan desapareció: `gestor` y `fuente` siguen en el catálogo y se activan
 * desde el selector.
 *
 * `vin`, `vehiculo`, `paso`, `estado`, `fechaCreacion` y `fechaActualizacion` quedan fuera por
 * otro motivo: su dato YA viaja apilado dentro de `placa`, `tramite` y `radicado` — activarlas
 * mueve el dato a su propia columna en vez de duplicarlo.
 */
export const DEFAULT_TRAMITES_VISIBLE_COLUMNS: readonly string[] = [
  'radicado',
  'placa',
  'propietario',
  'comprador',
  'tramite',
  'secretaria',
] as const;

const SELECT_COL_REM = 2.25;
const SELECT_COL_WIDTH = `${SELECT_COL_REM}rem`;
const SELECT_COL_MIN_PX = 36;
/** Acciones es de ancho fijo: dentro solo va el menú "Acciones", de tamaño constante. */
const ACTIONS_COL_PX = 150;
/** Píxeles de piso por cada `1fr` de crecimiento (ver `minPx` en TramitesColumnDef). */
const PX_POR_FR = 100;

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
 * Columnas visibles en el orden canónico del catálogo. Si `visibleKeys` no deja ninguna columna
 * conocida (preferencia corrupta o vacía), cae a TODAS: nunca se construye una tabla sin columnas.
 */
function resolveColumns(visibleKeys: readonly string[]): readonly TramitesColumnDef[] {
  const matched = TRAMITES_COLUMNS.filter((c) => visibleKeys.includes(c.key));
  return matched.length > 0 ? matched : TRAMITES_COLUMNS;
}

/**
 * Construye el `gridTemplateColumns` (y el ancho mínimo) a partir de las claves visibles. La
 * cabecera y cada fila de TramitesTable deben invocar ESTA MISMA función con el mismo arreglo
 * `visibleKeys`: al derivar ambas del mismo cálculo quedan alineadas por construcción sin
 * importar cuántas columnas se oculten — evita el desalineamiento cabecera/filas típico de un
 * grid con columnas condicionales calculadas dos veces por separado.
 *
 * Las columnas fijas entran como pista en `px`; las flexibles, en `fr` derivado de su piso.
 */
export function buildTramitesGridLayout(
  visibleKeys: readonly string[],
  options: BuildTramitesGridLayoutOptions = {},
): TramitesGridLayout {
  const includeSelectColumn = options.includeSelectColumn === true;
  const columns = resolveColumns(visibleKeys);
  const track = (c: TramitesColumnDef): string =>
    c.fixed ? `${c.minPx}px` : `${(c.minPx / PX_POR_FR).toFixed(2)}fr`;
  return {
    includeSelectColumn,
    gridTemplateColumns: [
      ...(includeSelectColumn ? [SELECT_COL_WIDTH] : []),
      ...columns.map(track),
      `${ACTIONS_COL_PX}px`,
    ].join(' '),
    // Suma de pisos: como el peso de cada flexible ES su piso, a este ancho todas las columnas
    // caen exactamente en su mínimo a la vez. Por debajo, scroll horizontal.
    minWidthPx:
      (includeSelectColumn ? SELECT_COL_MIN_PX : 0) +
      columns.reduce((sum, c) => sum + c.minPx, 0) +
      ACTIONS_COL_PX,
  };
}

/**
 * Construye el ancho de cada pista para el `<colgroup>` (Selección si aplica + visibles en orden
 * canónico + Acciones), en el mismo orden en que la tabla las pinta. `<colgroup>` no admite `fr`
 * (lo que usa `buildTramitesGridLayout`), así que aquí las pistas flexibles se expresan en
 * porcentaje y las fijas en su propia unidad.
 *
 * Los porcentajes se resuelven contra el ancho de la tabla, que se pinta a `width: 100%` del
 * contenedor: por eso ocultar columnas ya no la encoge — se reparte TODO el ancho disponible y
 * el `minWidthPx` solo actúa de piso (a partir de ahí, scroll horizontal).
 *
 * El reparto NO es del 100% pelado: las pistas fijas (Selección, las columnas `fixed` y Acciones)
 * se llevan lo suyo primero, y cada pista flexible descuenta con `calc()` su parte PROPORCIONAL
 * de todas ellas. Así el conjunto suma exactamente el ancho de la tabla —si se repartiera el 100%
 * íntegro entre las flexibles, la fila desbordaría por todo lo fijo— y el aire sobrante recae
 * solo en las columnas que lo aprovechan.
 */
export function buildTramitesColWidths(
  visibleKeys: readonly string[],
  options: BuildTramitesGridLayoutOptions = {},
): string[] {
  const includeSelectColumn = options.includeSelectColumn === true;
  const columns = resolveColumns(visibleKeys);

  // Si el usuario dejó visibles SOLO columnas fijas, no hay ninguna que absorba el sobrante: se
  // tratan todas como flexibles antes que devolver un reparto que no cubre el ancho de la tabla.
  const hayFlexibles = columns.some((c) => !c.fixed);
  const esFija = (c: TramitesColumnDef): boolean => c.fixed === true && hayFlexibles;

  const pxFijos = columns.reduce((sum, c) => sum + (esFija(c) ? c.minPx : 0), 0) + ACTIONS_COL_PX;
  const remFijos = includeSelectColumn ? SELECT_COL_REM : 0;
  const pesoFlexible = columns.reduce((sum, c) => sum + (esFija(c) ? 0 : c.minPx), 0);

  const anchoFlexible = (peso: number): string => {
    const percent = (peso / pesoFlexible) * 100;
    // Parte proporcional de lo fijo que cede esta columna; los descuentos suman lo fijo completo.
    const partes = [`${percent.toFixed(4)}%`, `${((percent / 100) * pxFijos).toFixed(2)}px`];
    if (remFijos > 0) partes.push(`${((percent / 100) * remFijos).toFixed(4)}rem`);
    return `calc(${partes.join(' - ')})`;
  };

  return [
    ...(includeSelectColumn ? [SELECT_COL_WIDTH] : []),
    ...columns.map((c) => (esFija(c) ? `${c.minPx}px` : anchoFlexible(c.minPx))),
    `${ACTIONS_COL_PX}px`,
  ];
}
