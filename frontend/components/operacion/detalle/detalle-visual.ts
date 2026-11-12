/**
 * Tokens visuales del modal Detalle / Ver trámite (`flit-detalle-tramite` mockup).
 * Fuente: `flit-2.0/.cursor/skills/flit-detalle-tramite/spec.md`
 */

export const DETALLE_BLUE = 'var(--color-flit-brand)';
export const DETALLE_NAVY = 'var(--di-heading)';
export const DETALLE_GREEN = 'var(--flit-success-ink)';
export const DETALLE_GOLD = 'var(--di-warning)';
export const DETALLE_RED = 'var(--destructive)';
export const DETALLE_GREY = 'var(--muted-foreground)';
export const DETALLE_META = 'var(--muted-foreground)';
export const DETALLE_BORDER = 'var(--border)';
export const DETALLE_CANVAS = 'var(--color-flit-bg)';

/**
 * CTA primario del detalle — token FLIT `gradient.primary`. Existe para que las acciones que
 * cambian el estado del trámite desde este modal (hoy: activar/continuar la subsanación) no
 * repitan el degradado como literal suelto, que es como se coló en otras pantallas.
 */
export const DETALLE_CTA_GRADIENT = 'var(--gradient-action)';

/** Card interior: `.card` de la propuesta (radio 20px, borde y sombra suaves). */
export const DETALLE_CARD =
  'rounded-[20px] border border-[var(--di-border)] bg-[var(--di-surface-elevated)] p-4 shadow-[var(--shadow-card)]';

export const DETALLE_OVERLAY_STYLE = {
  background: 'var(--overlay)',
  backdropFilter: 'blur(6px)',
  WebkitBackdropFilter: 'blur(6px)',
} as const;

export const DETALLE_SHEET_CLASS =
  'w-full max-w-6xl max-h-[92vh] overflow-y-auto rounded-[9px] border border-[color:var(--di-border)] bg-flit-bg p-5 shadow-none';
