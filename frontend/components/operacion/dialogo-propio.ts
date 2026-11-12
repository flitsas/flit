/**
 * Homologación #14507 (regla 3) — piezas visuales del `@flit/ui/Modal` para los diálogos propios que NO se
 * migran a él: cada uno conserva su componente, su foco, su tecla Escape y su clic afuera, y solo toma de
 * aquí el velo, el panel (borde de 1 px, sin sombra) y la cabecera blanca del UI Kit 2026. Son las mismas clases
 * que pinta `packages/ui/src/Modal.tsx`; si el Modal cambia, se cambian aquí.
 */

import { MODAL_VELO_CLS } from '@flit/ui/Modal';

/** Velo del fondo: el mismo del Modal (`MODAL_VELO_CLS`). */
export const DIALOGO_VELO = MODAL_VELO_CLS;

/** Panel: radio de diálogo, superficie de tarjeta y borde de 1 px, sin sombra (UI Kit 2026). El ancho lo pone cada diálogo. */
export const DIALOGO_PANEL =
  'relative overflow-clip rounded-[var(--di-radius-modal)] border border-[color:var(--di-border)] bg-card text-flit-primary shadow-none dark:text-foreground';

/** Cabecera blanca del kit (`.modal__franja`): ícono, título en --di-heading y X discreta. */
export const DIALOGO_FRANJA =
  'relative flex shrink-0 items-center gap-3 overflow-clip bg-transparent px-5 pb-1 pt-5 text-[color:var(--di-heading)] sm:px-6 sm:pt-6';

/** Círculo decorativo del diseño anterior: el kit no lo trae; el `<span aria-hidden>` queda oculto. */
export const DIALOGO_FRANJA_CIRCULO =
  'hidden';

/** Título dentro de la franja. */
export const DIALOGO_FRANJA_TITULO =
  'relative z-[1] min-w-0 flex-1 text-[1.375rem] font-bold leading-tight text-[color:var(--di-heading)]';

/** Ícono a la izquierda del título, dentro de la franja. */
export const DIALOGO_FRANJA_ICONO = 'relative z-[1] h-6 w-6 shrink-0 text-[color:var(--kit-azul-relleno)]';

/** Botón cerrar (X) de la franja. */
export const DIALOGO_FRANJA_CERRAR =
  'relative z-[1] grid h-9 w-9 shrink-0 place-items-center rounded-full bg-transparent text-[color:var(--di-text-tertiary)] transition hover:bg-[color:var(--kit-hover)] hover:text-[color:var(--di-action)] focus-visible:outline-2 focus-visible:outline-[var(--di-action)]';

/** Pie separado por la línea del Modal. */
export const DIALOGO_PIE = 'border-t border-[color:var(--di-divider)]';
