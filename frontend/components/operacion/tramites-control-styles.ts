import { buttonClass } from '@flit/ui/Button';

/**
 * Estilo ÚNICO de los controles de la fila de filtros del listado de trámites: búsqueda, Periodo,
 * + Filtro, Columnas, prioritarios y Actualizar.
 *
 * Existe porque esa fila tenía la misma cadena de clases copiada en tres sitios —`FILTRO_BTN_CLS` y
 * `COLS_BTN_CLS` en `TramitesFiltrosBar`, `CONTROL_CLS` en `TramitesListToolbar`— y las copias
 * fueron divergiendo: tres colores de texto distintos para controles que hacen lo mismo, y
 * "Columnas" sin altura fija, así que no medía los 36px de los demás y se veía descuadrada.
 *
 * Regla de color (desde HU #14476): todos los controles comparten el texto azul del botón
 * secundario del prototipo, y "este control tiene algo aplicado" se dice con el FONDO azul suave y
 * el borde de acción, igual en todos. Antes el azul del texto era la marca de "aplicado".
 */

/**
 * Control en reposo (HU #14476, manda la propuesta): el botón secundario del prototipo
 * (`buttonClass({ variant: 'secondary', size: 'sm' })`, píldora blanca con contorno de control y
 * texto de acción), con el alto fijo de 36px de esta fila. Lo comparten la barra de /tramites,
 * TablaFiltrosBar, HistorialPlaca, Validaciones y FiltroColumna: un solo aspecto en todas.
 *
 * Con el prototipo el texto ya es azul en reposo, así que "tiene algo aplicado" pasa a decirse con
 * el fondo azul suave y el borde de acción ({@link TRAMITES_CONTROL_ACTIVO_CLS}).
 */
export const TRAMITES_CONTROL_CLS = buttonClass({
  variant: 'secondary',
  size: 'sm',
  className: 'h-9 shrink-0 gap-1.5 px-3',
});

/**
 * Marcado de "tiene algo aplicado": fondo azul suave y borde de acción, como el chip pulsado del
 * prototipo. Va con `!` porque se concatena al de reposo sin resolver conflictos de clases.
 */
export const TRAMITES_CONTROL_ACTIVO_CLS =
  'bg-[color:var(--kit-azul-relleno)]! text-white!';

/**
 * Campo de texto o selector de la barra: `.control` del prototipo (contorno de control, radio de
 * 12px, superficie elevada) con el alto de 36px de la fila.
 */
export const TRAMITES_INPUT_CLS =
  'h-10 rounded-[9px] border border-[color:var(--di-border-control)] bg-[var(--di-surface-elevated)] px-3 text-sm ' +
  'text-[color:var(--di-text-primary)] placeholder:text-[color:var(--kit-gris)] outline-none transition ' +
  'focus:border-[color:var(--di-action)] focus:shadow-[var(--kit-sombra)] focus:outline-none ' +
  'focus-visible:ring-2 focus-visible:ring-[var(--di-action)]';

/** Clase del control según tenga o no algo aplicado. */
export function controlCls(activo: boolean): string {
  return activo ? `${TRAMITES_CONTROL_CLS} ${TRAMITES_CONTROL_ACTIVO_CLS}` : TRAMITES_CONTROL_CLS;
}
