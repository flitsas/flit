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

export const OT_NAVY = "var(--color-flit-primary)";
export const OT_BLUE = "var(--color-flit-brand)";
export const OT_GREEN = "var(--flit-success-ink)";
export const OT_WARN = "var(--badge-warning-fg)";
/** Texto sobre la banda ámbar de avisos: el ámbar puro sobre blanco no contrasta. */
export const OT_WARN_TEXT = "var(--badge-warning-fg)";
/** Naranja del rechazo. El prototipo usa `#FF6B00` en el botón y `#FF5722` en la modal de motivo. */
export const OT_ORANGE = "var(--destructive)";
export const OT_BORDER = "var(--border)";
export const OT_CANVAS = "var(--secondary)";
export const OT_META = "var(--muted-foreground)";

/** Tarjeta del prototipo: sin relleno propio —cada bloque decide el suyo—. */
export const OT_CARD =
  "rounded-xl border border-border bg-card shadow-[0_4px_12px_rgba(0,0,0,0.05)]";

/** Marco de las rejillas de datos: recorta las bandas de encabezado a las esquinas redondeadas. */
export const OT_MARCO =
  "overflow-hidden rounded-xl border border-border";

/** Banda de encabezado de una rejilla. */
export const OT_BANDA = "bg-secondary";

/**
 * Degradado del CTA de aprobación. El detalle del gestor usa el degradado de marca (azul→cian); el
 * prototipo del OT usa cian→esmeralda y esa es la decisión de producto para esta pantalla.
 */
export const OT_APROBAR_GRADIENTE = "bg-[image:var(--gradient-action)]";

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
  "flex w-[900px] max-w-full max-h-[85vh] flex-col overflow-hidden rounded-2xl bg-secondary p-4 shadow-[var(--shadow-modal)]";
