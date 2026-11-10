/**
 * Campos de las Validaciones manuales legibles en tema claro y oscuro (capa dark de la línea base:
 * fondo #162744, texto #FFFFFF, borde rgba(255,255,255,0.15), foco #7C9BFF). `WIZARD_SELECT` fuerza
 * `bg-white` y `color-scheme: light` para el wizard; aquí se sobrescribe solo en oscuro, sin tocarlo.
 * En claro el aspecto es el mismo de siempre.
 */
export const MANUAL_SELECT_CLASS =
  'border-border dark:bg-card dark:text-foreground dark:[color-scheme:dark] ' +
  'dark:hover:border-ring dark:focus:border-ring dark:focus:ring-ring/30';

export const MANUAL_INPUT_CLASS =
  'border-border dark:bg-card dark:text-foreground dark:placeholder:text-muted-foreground ' +
  'dark:focus:border-ring dark:focus:ring-ring/30';

/**
 * Avisos en línea del detalle y los diálogos: la forma tintada de `color.badge` (fondo translúcido + texto + borde) de
 * la línea base, en claro y en oscuro (capa `dark.badge`). Nunca fondo sólido con texto blanco.
 */
export const MANUAL_AVISO = {
  info:
    'border-[var(--badge-info-border)] bg-[var(--badge-info-bg)] text-flit-brand-ink',
  success:
    'border-[var(--badge-success-border)] bg-[var(--badge-success-bg)] text-[var(--badge-success-fg)]',
  warning:
    'border-[var(--badge-warning-border)] bg-[var(--badge-warning-bg)] text-[var(--badge-warning-fg)]',
  danger:
    'border-[var(--badge-danger-border)] bg-[var(--badge-danger-bg)] text-[var(--badge-danger-fg)]',
} as const;

/** Forma común de un aviso (tarjeta de radio 18px, texto 14px). */
export const MANUAL_AVISO_BASE = 'flex items-start gap-2 rounded-2xl border px-4 py-3 text-sm';

/** Foco de teclado: anillo de 2px #557EFF (#7C9BFF en oscuro) con desfase de 2px. */
export const MANUAL_FOCO =
  'focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-flit-brand';

/** Botón secundario de pastilla (el «Cancelar» / «Cerrar» de los diálogos FLIT): borde de marca, navy / blanco. */
export const MANUAL_BTN_SECUNDARIO =
  'rounded-full border border-border px-5 py-2.5 text-sm font-semibold text-foreground transition hover:bg-secondary disabled:cursor-not-allowed disabled:opacity-70 ' +
  MANUAL_FOCO;
