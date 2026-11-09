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
 * Fondo de la barra de cabecera y color de su texto. Salen de los tokens (`--table-header-*`): en el UI kit
 * «Diseño Inteligente» (HU #14476) la cabecera es tinta con texto blanco y en oscuro se adapta sola.
 */
export const TABLA_HEADER_BG = 'var(--table-header-bg)';
export const TABLA_HEADER_FG = 'var(--table-header-fg)';

/**
 * Celda de cabecera: versalita de 10px y barra FIJA al hacer scroll.
 *
 * El `sticky` no es un adorno: en una lista larga —el OT pagina de 20 en 20— al bajar se pierde de
 * vista qué columna se está mirando, y en una tabla de diez columnas eso obliga a subir a releer.
 * Necesita fondo opaco (lo pone el `style` con {@link TABLA_HEADER_BG}) o las filas se
 * transparentarían por debajo.
 */
export const TABLA_HEADER_CELL_CLS =
  'sticky top-0 z-10 px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide';

/**
 * Fila de listado: al pasar el puntero toma la superficie hundida del kit (`--table-row-hover`), como en
 * el UI kit «Diseño Inteligente» (HU #14476). Se conserva la sombra suave, que es la que hace que la fila
 * se lea como una tarjeta que se puede abrir; el borde no cambia, para no competir con el de estado.
 */
export const TABLA_ROW_HOVER_CLS =
  'transition hover:bg-[var(--table-row-hover)] hover:shadow-[var(--shadow-card-hover)]';

/** Línea secundaria dentro de una celda (fechas bajo el radicado, gestor bajo la empresa…). */
export const TABLA_CELDA_SECUNDARIA_CLS = 'text-[10px] opacity-55';
