/**
 * Clases de inputs OT — alineadas con el buscador homologado de `OtMandatosSection.tsx`
 * (HU #12883). Antes usaba `bg-transparent`: sobre el fondo azul claro de la app el campo se veía
 * "lavado"/sin contraste. El guardián de diseño exige fondo blanco (`color.background.card`
 * #FFFFFF), borde #DFE5ED, texto navy #162744 y foco visible de 2px #557EFF con offset —
 * `outline-none` sin sustituto es una desviación bloqueante, así que solo se apaga en
 * `focus-visible` y se reemplaza por el anillo. El tema oscuro sale de la capa `dark` del token
 * file (`dark.background.card` = #162744, `dark.border.input` = rgba(255,255,255,0.15)): NO
 * `#0B0F14`, que es un surface inventado por componente y no está autorizado.
 */
export const OT_INPUT_CLS =
  "w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-xs text-[#162744] placeholder:text-[#59677D]/70 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-70 dark:border-white/15 dark:bg-[#162744] dark:text-white";

export const OT_FILTER_FORM_CLS =
  "grid grid-cols-1 gap-3 rounded-2xl border bg-white p-4 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 dark:bg-[#162744]";

/**
 * Rótulo de un campo del panel de búsqueda: versalita corta y atenuada sobre el campo, como en el
 * diseño. Antes era del mismo tamaño y peso que el contenido, así que en una rejilla de ocho campos
 * los rótulos competían con los valores en vez de ordenarlos.
 */
export const OT_FILTER_LABEL_CLS =
  "flex flex-col gap-1 text-[10px] font-semibold uppercase tracking-wide text-foreground/60";
