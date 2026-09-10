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

/** Gris de la barra de cabecera y color de su texto. */
export const TABLA_HEADER_BG = '#DFE5ED';
export const TABLA_HEADER_FG = '#162744';

/**
 * Celda de cabecera: versalita de 10px y barra FIJA al hacer scroll.
 *
 * El `sticky` no es un adorno: en una lista larga —el OT pagina de 20 en 20— al bajar se pierde de
 * vista qué columna se está mirando, y en una tabla de diez columnas eso obliga a subir a releer.
 * Necesita fondo opaco (lo pone el `style` con {@link TABLA_HEADER_BG}) o las filas se
 * transparentarían por debajo.
 */
export const TABLA_HEADER_CELL_CLS =
  'sticky top-0 z-10 px-4 py-2.5 text-left text-[10px] font-semibold uppercase tracking-wider';

/**
 * Fila de listado: tarjeta blanca que se ilumina al pasar el puntero.
 *
 * El realce es una SOMBRA azul, no un cambio de borde: la fila conserva su contorno neutro y lo que
 * cambia es la elevación, que es lo que hace que se lea como una tarjeta que se puede abrir. El
 * borde azul que usaba el gestor competía con el borde de estado y decía otra cosa.
 */
export const TABLA_ROW_HOVER_CLS =
  'transition hover:shadow-[0_8px_24px_rgba(85,126,255,0.18)]';

/** Línea secundaria dentro de una celda (fechas bajo el radicado, gestor bajo la empresa…). */
export const TABLA_CELDA_SECUNDARIA_CLS = 'text-[10px] opacity-55';
