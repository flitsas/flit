/**
 * Estilo COMPARTIDO de las tablas de listado (trámites del gestor y bandeja del OT).
 *
 * Existe porque las dos pantallas dibujan la misma tabla del diseño y habían divergido cada una por
 * su lado: la del gestor con la cabecera a 12px, la del OT con un token de gris distinto, ninguna
 * con la cabecera fija, y el hover resuelto de dos formas que no eran la del diseño. Teniendo dos
 * copias del mismo estilo, la siguiente edición volvía a separarlas.
 *
 * Los valores salen del repo de diseño (`flit-2.0`), donde la cabecera es LITERALMENTE la misma
 * línea en `Tramites.tsx` (gestor) y en `OTTramites.tsx` (OT).
 */

/**
 * Fondo de la barra de cabecera y color de su texto. Salen de los tokens (`--table-header-*`): desde la HU #14477 la
 * cabecera es la tinta del UI kit (`.table th`: #162744 con texto blanco), por decisión de producto sobre la cabecera
 * clara del prototipo; en oscuro se adapta sola. Toda `<th>` lleva LOS DOS (fondo y texto): con solo el fondo, el
 * texto heredado queda oscuro sobre tinta.
 */
export const TABLA_HEADER_BG = 'var(--table-header-bg)';
export const TABLA_HEADER_FG = 'var(--table-header-fg)';

/**
 * Celda de cabecera (HU #14477): tinta del UI kit con texto blanco de 13px en peso 600, sin versalitas (producto
 * pidió solo el color del kit, no sus mayúsculas), y extremos de 8px. La barra queda FIJA al hacer scroll.
 *
 * El `sticky` no es un adorno: en una lista larga —el OT pagina de 20 en 20— al bajar se pierde de
 * vista qué columna se está mirando, y en una tabla de diez columnas eso obliga a subir a releer.
 * Necesita fondo opaco (lo pone el `style` con {@link TABLA_HEADER_BG}) o las filas se
 * transparentarían por debajo. El redondeo de extremos va con `first:`/`last:`, así vale aunque la
 * pantalla haya escrito su propio `rounded-l-xl` (las variantes ganan sobre la clase suelta).
 */
export const TABLA_HEADER_CELL_CLS =
  'sticky top-0 z-10 px-3.5 py-2.5 text-left text-[0.8125rem] font-semibold normal-case tracking-normal ' +
  'first:rounded-l-lg last:rounded-r-lg';

/**
 * Tarjeta detrás de la tabla, como la `.card` del prototipo (HU #14477): superficie elevada, borde, radio de 20px y
 * sombra suave. Va en el envoltorio de la tabla (el de scroll o el que la junta con su paginador), nunca dentro de otra
 * tarjeta. Anula la regla anterior de «tabla sin tarjeta ni fondo detrás», por decisión de producto.
 *
 * Por debajo de 900px desaparece: allí cada fila ya es una tarjeta, y una tarjeta grande alrededor de tarjetas
 * pequeñas sería ruido y le restaría ancho a la rejilla. El relleno vertical es menor que el lateral porque la
 * separación de 6px entre filas (`border-spacing-y-1.5`) ya deja aire arriba y abajo.
 */
export const TABLA_TARJETA_SUPERFICIE_CLS =
  'rounded-[20px] border border-[color:var(--di-border)] bg-[var(--di-surface-elevated)] px-4 py-2.5 ' +
  'shadow-[var(--shadow-card)]';
export const TABLA_TARJETA_CLS =
  `${TABLA_TARJETA_SUPERFICIE_CLS} ` +
  'max-[900px]:rounded-none max-[900px]:border-0 max-[900px]:bg-transparent max-[900px]:p-0 max-[900px]:shadow-none';
/**
 * Variante para el listado de /tramites, cuyas filas pasan a tarjeta a 640px (`tramites-propuesta.css`) y no a 900px:
 * la tarjeta grande se mantiene hasta ese mismo corte, para no dejar un tramo sin ninguna de las dos.
 */
export const TABLA_TARJETA_640_CLS =
  `${TABLA_TARJETA_SUPERFICIE_CLS} ` +
  'max-[640px]:rounded-none max-[640px]:border-0 max-[640px]:bg-transparent max-[640px]:p-0 max-[640px]:shadow-none';

/**
 * Fila de listado: al pasar el puntero cambia el BORDE de sus celdas (`--di-border-control`), no el fondo,
 * como en el prototipo: así las pastillas de estado conservan su contraste. Sin sombra ni elevación.
 */
export const TABLA_ROW_HOVER_CLS =
  'transition-colors [&:hover>td]:border-[color:var(--di-border-control)]!';

/** Línea secundaria dentro de una celda (fechas bajo el radicado, gestor bajo la empresa…). */
export const TABLA_CELDA_SECUNDARIA_CLS = 'text-[10px] opacity-55';

/*
 * HU #14477 — la tabla como tarjetas por debajo de 900px (tableta y celular), como la media query de tabla del
 * prototipo «Diseño Inteligente». Solo CSS: la cabecera queda para lectores de pantalla, cada fila es una tarjeta
 * en una rejilla de 300px, la primera celda hace de título y las demás muestran su columna con `data-label`.
 * Las usan `DataTable` y las tablas escritas a mano; ninguna reordena el DOM.
 */
/** En la `<table>`: deja de ser tabla y anula cualquier ancho mínimo (también el de un `style`). */
export const TABLA_MOVIL_TABLE_CLS = 'max-[900px]:block max-[900px]:min-w-0! max-[900px]:w-full!';
/** En el `<thead>`: solo para lectores de pantalla. */
export const TABLA_MOVIL_THEAD_CLS = 'max-[900px]:sr-only';
/** En el `<tbody>`: rejilla de tarjetas de 300px. */
export const TABLA_MOVIL_TBODY_CLS =
  'max-[900px]:grid max-[900px]:grid-cols-[repeat(auto-fill,minmax(min(300px,100%),1fr))] max-[900px]:gap-2.5';
/** En el `<tr>` de datos: la tarjeta; su borde es el que cambia al pasar el puntero. */
export const TABLA_MOVIL_ROW_CLS =
  'max-[900px]:block max-[900px]:rounded-xl max-[900px]:border max-[900px]:border-[color:var(--di-border)] ' +
  'max-[900px]:bg-[var(--di-surface-elevated)] max-[900px]:px-3.5 max-[900px]:pt-1 max-[900px]:pb-2 ' +
  'max-[900px]:hover:border-[color:var(--di-border-control)]';
/**
 * En cada `<td>` (con `data-label`): etiqueta de la columna a la izquierda, valor a la derecha. El borde y el
 * redondeo van con `!` porque las recetas de escritorio los ponen con `first:`/`last:`, que pesan más.
 */
export const TABLA_MOVIL_CELL_CLS =
  'max-[900px]:flex max-[900px]:items-center max-[900px]:justify-between max-[900px]:gap-3 ' +
  'max-[900px]:rounded-none! max-[900px]:border-0! max-[900px]:px-0 max-[900px]:py-[5px] max-[900px]:text-right ' +
  'max-[900px]:text-[0.8125rem] max-[900px]:bg-transparent max-[900px]:before:shrink-0 max-[900px]:before:text-left ' +
  'max-[900px]:before:text-xs max-[900px]:before:font-medium max-[900px]:before:text-[color:var(--di-text-tertiary)] ' +
  'max-[900px]:before:content-[attr(data-label)]';
/** Además, en la primera `<td>`: hace de título de la tarjeta, con un divisor debajo (sombra interior: el borde está anulado). */
export const TABLA_MOVIL_CELL_TITULO_CLS =
  'max-[900px]:mb-1 max-[900px]:shadow-[inset_0_-1px_0_var(--di-divider)] max-[900px]:py-2.5 ' +
  'max-[900px]:text-sm max-[900px]:font-semibold';
/** En una fila a todo el ancho (vacío, expandida, `colSpan`) y en su `<td>`: ocupa la rejilla entera. */
export const TABLA_MOVIL_FILA_ANCHA_CLS = 'max-[900px]:col-span-full max-[900px]:block';
