/**
 * Estilo ÚNICO de los controles de la fila de filtros del listado de trámites: búsqueda, Periodo,
 * + Filtro, Columnas, prioritarios y Actualizar.
 *
 * Existe porque esa fila tenía la misma cadena de clases copiada en tres sitios —`FILTRO_BTN_CLS` y
 * `COLS_BTN_CLS` en `TramitesFiltrosBar`, `CONTROL_CLS` en `TramitesListToolbar`— y las copias
 * fueron divergiendo: tres colores de texto distintos para controles que hacen lo mismo, y
 * "Columnas" sin altura fija, así que no medía los 36px de los demás y se veía descuadrada.
 *
 * Regla de color: **el azul significa "este control tiene algo aplicado", y nada más.** En reposo
 * todos los controles son neutros. Antes "Columnas" venía azul de fábrica, con lo que el mismo azul
 * que en "Periodo" quería decir "hay un filtro activo" en "Columnas" no quería decir nada.
 */

/** Control en reposo: 36px de alto, texto de 12px semibold, borde y fondo neutros. */
export const TRAMITES_CONTROL_CLS =
  'inline-flex h-9 shrink-0 items-center gap-1.5 whitespace-nowrap rounded-xl border ' +
  'border-[#DFE5ED] bg-white px-3 text-xs font-semibold text-[#1E293B] transition ' +
  'hover:bg-[#EFF6FF] focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] ' +
  'focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50 ' +
  'dark:border-white/15 dark:bg-[#0B0F14] dark:text-white';

/**
 * Marcado de "tiene algo aplicado": borde de marca y texto en el azul OSCURECIDO (`#3B4FD6`), no en
 * el `#557EFF` puro — sobre blanco el puro se queda en 3.7:1 y este es texto pequeño.
 * Se concatena al de reposo, que ya aporta la geometría.
 */
export const TRAMITES_CONTROL_ACTIVO_CLS = 'border-[#557EFF] text-[#3B4FD6] dark:text-[#8FA8FF]';

/** Clase del control según tenga o no algo aplicado. */
export function controlCls(activo: boolean): string {
  return activo ? `${TRAMITES_CONTROL_CLS} ${TRAMITES_CONTROL_ACTIVO_CLS}` : TRAMITES_CONTROL_CLS;
}
