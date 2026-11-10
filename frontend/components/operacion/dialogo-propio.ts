/**
 * Homologación #14507 (regla 3) — piezas visuales del `@flit/ui/Modal` para los diálogos propios que NO se
 * migran a él: cada uno conserva su componente, su foco, su tecla Escape y su clic afuera, y solo toma de
 * aquí el velo, el panel (radio, sombra) y la franja tinta con el círculo turquesa. Son las mismas clases
 * que pinta `packages/ui/src/Modal.tsx`; si el Modal cambia, se cambian aquí.
 */

/** Velo del fondo: `--overlay` con el desenfoque del Modal. */
export const DIALOGO_VELO = 'bg-[var(--overlay)] backdrop-blur-md';

/** Panel: radio de diálogo, superficie de tarjeta y sombra de modal. El ancho lo pone cada diálogo. */
export const DIALOGO_PANEL =
  'relative overflow-clip rounded-[var(--di-radius-modal)] bg-card text-flit-primary shadow-[var(--shadow-modal)] dark:text-foreground';

/** Franja tinta (`.modal__franja`): ícono, título y X en el color sobre acento. */
export const DIALOGO_FRANJA =
  'relative flex shrink-0 items-center gap-3 overflow-clip bg-[color:var(--di-accent)] px-5 py-4 text-[color:var(--di-on-accent)] sm:px-6 sm:py-5';

/** Círculo turquesa decorativo de la franja (va como `<span aria-hidden>` dentro de ella). */
export const DIALOGO_FRANJA_CIRCULO =
  'pointer-events-none absolute -right-[30px] -top-[60px] h-[120px] w-[120px] rounded-full bg-[color:var(--di-highlight-decor)]';

/** Título dentro de la franja. */
export const DIALOGO_FRANJA_TITULO =
  'relative z-[1] min-w-0 flex-1 text-base font-semibold leading-tight text-[color:var(--di-on-accent)]';

/** Ícono a la izquierda del título, dentro de la franja. */
export const DIALOGO_FRANJA_ICONO = 'relative z-[1] h-6 w-6 shrink-0 text-[color:var(--di-on-accent)]';

/** Botón cerrar (X) de la franja. */
export const DIALOGO_FRANJA_CERRAR =
  'relative z-[1] grid h-10 w-10 shrink-0 place-items-center rounded-full bg-[color-mix(in_srgb,var(--di-on-accent)_18%,transparent)] text-[color:var(--di-on-accent)] transition hover:bg-[color-mix(in_srgb,var(--di-on-accent)_30%,transparent)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--di-on-accent)]';

/** Pie separado por la línea del Modal. */
export const DIALOGO_PIE = 'border-t border-[color:var(--di-divider)]';
