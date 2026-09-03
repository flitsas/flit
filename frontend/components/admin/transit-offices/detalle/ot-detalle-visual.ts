/**
 * Tokens visuales del detalle del trámite del ORGANISMO DE TRÁNSITO (HU #12060).
 *
 * Es una COPIA deliberada de `components/operacion/detalle/detalle-visual.ts`, no un import.
 *
 * Los dos ficheros nacen del mismo prototipo y hoy valen lo mismo token a token, así que compartirlos
 * parecía lo correcto. No lo es: el detalle del OT y el del gestor pasaron a evolucionar por separado
 * —el del OT sigue el prototipo `flit-2.0/src/components/atom/ot/OTDetalleModal.tsx`, el del gestor
 * no— y con un módulo común cualquier ajuste de uno se cuela en la pantalla del otro sin que nadie
 * lo pida. Duplicar aquí es lo que hace verdadera la promesa de que el gestor no se toca.
 */

export const OT_NAVY = "#162744";
export const OT_BLUE = "#557EFF";
export const OT_GREEN = "#8CC63F";
export const OT_WARN = "#F9AC00";
/** Texto sobre la banda ámbar de avisos: el ámbar puro sobre blanco no contrasta. */
export const OT_WARN_TEXT = "#8A6300";
/** Naranja del rechazo. El prototipo usa `#FF6B00` en el botón y `#FF5722` en la modal de motivo. */
export const OT_ORANGE = "#FF6B00";
export const OT_BORDER = "#DFE5ED";
export const OT_CANVAS = "#EEF5FF";
export const OT_META = "#5E6A7B";

/** Tarjeta del prototipo: sin relleno propio —cada bloque decide el suyo—. */
export const OT_CARD =
  "rounded-xl border border-[#DFE5ED] bg-white shadow-[0_4px_12px_rgba(0,0,0,0.05)] dark:border-white/5 dark:bg-[#0B0F14]";

/** Marco de las rejillas de datos: recorta las bandas de encabezado a las esquinas redondeadas. */
export const OT_MARCO =
  "overflow-hidden rounded-xl border border-[#DFE5ED] dark:border-white/5";

/** Banda de encabezado de una rejilla. */
export const OT_BANDA = "bg-[#EEF5FF] dark:bg-white/5";

/**
 * Degradado del CTA de aprobación. El detalle del gestor usa el degradado de marca (azul→cian); el
 * prototipo del OT usa cian→esmeralda y esa es la decisión de producto para esta pantalla.
 */
export const OT_APROBAR_GRADIENTE = "bg-gradient-to-r from-cyan-500 to-emerald-500";

export const OT_OVERLAY_STYLE = {
  background: "rgba(22, 39, 68, 0.45)",
  backdropFilter: "blur(6px)",
  WebkitBackdropFilter: "blur(6px)",
} as const;

/**
 * Hoja del modal. A diferencia de la del gestor —que crece hasta `max-w-6xl` y desplaza la hoja
 * entera— esta se fija en 900px y reparte la altura en tres: encabezado y pie quietos, cuerpo
 * desplazable. Con los acordeones abiertos, los botones de decidir siguen a la vista.
 */
export const OT_SHEET_CLASS =
  "flex w-[900px] max-w-full max-h-[85vh] flex-col overflow-hidden rounded-2xl bg-[#EEF5FF] p-4 shadow-[0_20px_48px_rgba(15,23,20,0.18)] dark:bg-[#05060A]";
