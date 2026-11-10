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
 * Fondo de la barra de cabecera y color de su texto. Salen de los tokens (`--table-header-*`): en el prototipo
 * «Diseño Inteligente» (HU #14477) la cabecera es la superficie hundida con texto secundario, y en oscuro se adapta sola.
 */
export const TABLA_HEADER_BG = 'var(--table-header-bg)';
export const TABLA_HEADER_FG = 'var(--table-header-fg)';

/**
 * Celda de cabecera, como el `th` del prototipo «Diseño Inteligente» (HU #14477): superficie hundida, texto
 * secundario de 13px en peso 600, sin versalitas, y extremos de 8px. La barra queda FIJA al hacer scroll.
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
 * Fila de listado: al pasar el puntero cambia el BORDE de sus celdas (`--di-border-control`), no el fondo,
 * como en el prototipo: así las pastillas de estado conservan su contraste. Sin sombra ni elevación.
 */
export const TABLA_ROW_HOVER_CLS =
  'transition-colors [&:hover>td]:border-[color:var(--di-border-control)]!';

/** Línea secundaria dentro de una celda (fechas bajo el radicado, gestor bajo la empresa…). */
export const TABLA_CELDA_SECUNDARIA_CLS = 'text-[10px] opacity-55';
