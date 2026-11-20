/** Clases de los filtros de la cabecera del Inicio (Rango de fechas y Compañía), compartidas por las 3 vistas. */

/**
 * HU #14481 — los dos filtros de la cabecera (Rango de fechas y Compañía) con el `.control` del
 * prototipo: 44 px, contorno de control visible, superficie elevada y la etiqueta como `.campo
 * label` (13 px, 600, sin versalitas). Son átomos compartidos que solo aceptan `className` en la
 * raíz, así que el aspecto se aplica desde aquí a su campo (`>button` del selector de fechas,
 * `input` del buscador de compañía) sin tocarlos.
 */
export const FILTRO_CONTROL_CLS =
  "[&>label]:text-[0.8125rem] [&>label]:font-semibold [&>label]:normal-case [&>label]:opacity-100 " +
  "[&>label]:text-[color:var(--di-text-primary)] " +
  "[&>button]:min-h-10 [&>button]:rounded-[9px] [&>button]:border-[color:var(--di-border-control)] " +
  "[&>button]:bg-[var(--di-surface-elevated)] [&>button]:text-[color:var(--di-text-primary)] " +
  "[&_input]:h-10 [&_input]:rounded-[9px] [&_input]:border-[color:var(--di-border-control)] " +
  "[&_input]:bg-[var(--di-surface-elevated)] [&_input]:text-[color:var(--di-text-primary)]";

/**
 * UI Kit 2026 — rejilla de los filtros de la cabecera, con el mismo hueco que las cifras de abajo. `RangoFechas` es un
 * átomo compartido: su raíz pasa a `display: contents` para que Periodo, Desde y Hasta sean celdas de esta rejilla
 * (Periodo junto a Compañía; Desde y Hasta en la fila siguiente, una columna cada uno) y sus campos toman el control
 * del kit (`TRAMITES_INPUT_CLS`: 40 px, radio 9 px, contorno de control). Sus estados y manejadores no cambian.
 */
export const FILTROS_REJILLA_CLS =
  "grid grid-cols-2 gap-3 items-end [&>[data-testid=rango-fechas]]:contents " +
  "[&>[data-testid=rango-fechas]>label]:min-w-0 [&>[data-testid=rango-fechas]>p]:col-span-2 " +
  "[&>[data-testid=rango-fechas]>label]:text-[0.8125rem] [&>[data-testid=rango-fechas]>label]:font-semibold " +
  "[&>[data-testid=rango-fechas]>label]:text-[color:var(--di-text-primary)] " +
  "[&>[data-testid=rango-fechas]_:is(select,input)]:h-10 [&>[data-testid=rango-fechas]_:is(select,input)]:w-full " +
  "[&>[data-testid=rango-fechas]_:is(select,input)]:min-w-0 [&>[data-testid=rango-fechas]_:is(select,input)]:rounded-[9px] " +
  "[&>[data-testid=rango-fechas]_:is(select,input)]:border-[color:var(--di-border-control)] " +
  "[&>[data-testid=rango-fechas]_:is(select,input)]:bg-[var(--di-surface-elevated)] " +
  "[&>[data-testid=rango-fechas]_:is(select,input)]:px-3 [&>[data-testid=rango-fechas]_:is(select,input)]:text-base " +
  // Responsive iOS: 16px en móvil (iOS hace zoom al enfocar un campo de menos de 16px); desde sm vuelve a 14px.
  "sm:[&>[data-testid=rango-fechas]_:is(select,input)]:text-sm " +
  "[&>[data-testid=rango-fechas]_:is(select,input)]:text-[color:var(--di-text-primary)] " +
  "[&>[data-testid=rango-fechas]_:is(select,input)]:focus:border-[color:var(--di-action)]";
