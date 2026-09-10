/**
 * Tokens visuales del modal Detalle / Ver trámite (`flit-detalle-tramite` mockup).
 * Fuente: `flit-2.0/.cursor/skills/flit-detalle-tramite/spec.md`
 */

export const DETALLE_BLUE = '#557EFF';
export const DETALLE_NAVY = '#162744';
export const DETALLE_GREEN = '#8CC63F';
export const DETALLE_GOLD = '#F9AC00';
export const DETALLE_RED = '#FF4E00';
export const DETALLE_GREY = '#94A3B8';
export const DETALLE_META = '#5E6A7B';
export const DETALLE_BORDER = '#DFE5ED';
export const DETALLE_CANVAS = '#EEF5FF';

/**
 * CTA primario del detalle — token FLIT `gradient.primary`. Existe para que las acciones que
 * cambian el estado del trámite desde este modal (hoy: activar/continuar la subsanación) no
 * repitan el degradado como literal suelto, que es como se coló en otras pantallas.
 */
export const DETALLE_CTA_GRADIENT = 'linear-gradient(135deg, #557EFF 0%, #00DBD5 100%)';

/** Card interior: radius 12px, sombra mockup, dark #0B0F14 */
export const DETALLE_CARD =
  'rounded-xl border border-[#DFE5ED] bg-white p-4 shadow-[0_4px_12px_rgba(0,0,0,0.05)] dark:border-white/5 dark:bg-[#0B0F14]';

export const DETALLE_OVERLAY_STYLE = {
  background: 'rgba(22, 39, 68, 0.45)',
  backdropFilter: 'blur(6px)',
  WebkitBackdropFilter: 'blur(6px)',
} as const;

export const DETALLE_SHEET_CLASS =
  'w-full max-w-6xl max-h-[92vh] overflow-y-auto rounded-2xl bg-[#EEF5FF] p-5 shadow-[0_20px_48px_rgba(15,23,20,0.18)] dark:bg-[#05060A]';
