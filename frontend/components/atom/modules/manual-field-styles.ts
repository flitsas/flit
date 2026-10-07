/**
 * Campos de las Validaciones manuales legibles en tema claro y oscuro (capa dark de la línea base:
 * fondo #162744, texto #FFFFFF, borde rgba(255,255,255,0.15), foco #7C9BFF). `WIZARD_SELECT` fuerza
 * `bg-white` y `color-scheme: light` para el wizard; aquí se sobrescribe solo en oscuro, sin tocarlo.
 * En claro el aspecto es el mismo de siempre.
 */
export const MANUAL_SELECT_CLASS =
  'border-[#DFE5ED] dark:border-white/15 dark:bg-[#162744] dark:text-white dark:[color-scheme:dark] ' +
  'dark:hover:border-[#7C9BFF] dark:focus:border-[#7C9BFF] dark:focus:ring-[#7C9BFF]/30';

export const MANUAL_INPUT_CLASS =
  'border-[#DFE5ED] dark:border-white/15 dark:bg-[#162744] dark:text-white dark:placeholder:text-white/70 ' +
  'dark:focus:border-[#7C9BFF] dark:focus:ring-[#7C9BFF]/30';

/**
 * Avisos en línea del detalle y los diálogos: la forma tintada de `color.badge` (fondo translúcido + texto + borde) de
 * la línea base, en claro y en oscuro (capa `dark.badge`). Nunca fondo sólido con texto blanco.
 */
export const MANUAL_AVISO = {
  info:
    'border-[#557EFF]/35 bg-[#557EFF]/[0.14] text-[#3B4FD6] dark:border-[#557EFF]/40 dark:bg-[#557EFF]/[0.22] dark:text-[#A5B8FF]',
  success:
    'border-[#CDEB9C] bg-[#F3FBE8] text-[#4F7A12] dark:border-[#8CC63F]/45 dark:bg-[#8CC63F]/[0.22] dark:text-[#B8E986]',
  warning:
    'border-[#F9AC00]/40 bg-[#F9AC00]/15 text-[#B45309] dark:border-[#F9AC00]/40 dark:bg-[#F9AC00]/20 dark:text-[#FBBF24]',
  danger:
    'border-[#FF4E00]/[0.32] bg-[#FF4E00]/[0.12] text-[#C2410C] dark:border-[#FF4E00]/40 dark:bg-[#FF4E00]/20 dark:text-[#FCA574]',
} as const;

/** Forma común de un aviso (tarjeta de radio 18px, texto 14px). */
export const MANUAL_AVISO_BASE = 'flex items-start gap-2 rounded-2xl border px-4 py-3 text-sm';

/** Foco de teclado: anillo de 2px #557EFF (#7C9BFF en oscuro) con desfase de 2px. */
export const MANUAL_FOCO =
  'focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF] dark:focus-visible:outline-[#7C9BFF]';

/** Botón secundario de pastilla (el «Cancelar» / «Cerrar» de los diálogos FLIT): borde de marca, navy / blanco. */
export const MANUAL_BTN_SECUNDARIO =
  'rounded-full border border-[#DFE5ED] px-5 py-2.5 text-sm font-semibold text-[#162744] transition hover:bg-[#F4F8FF] disabled:cursor-not-allowed disabled:opacity-70 ' +
  'dark:border-white/15 dark:text-white dark:hover:bg-white/5 ' +
  MANUAL_FOCO;
