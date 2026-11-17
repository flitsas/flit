/**
 * Homologación #14507 (regla 3) — piezas visuales del `@flit/ui/Modal` para los diálogos propios que NO se
 * migran a él: cada uno conserva su componente, su foco, su tecla Escape y su clic afuera, y solo toma de
 * aquí el velo, el panel (borde de 1 px, sin sombra) y la cabecera blanca del UI Kit 2026. Son las mismas clases
 * que pinta `packages/ui/src/Modal.tsx`; si el Modal cambia, se cambian aquí.
 */

import { MODAL_CABECERA_CLS, MODAL_CERRAR_CLS, MODAL_TITULO_CLS, MODAL_VELO_CLS } from '@flit/ui/Modal';

/** Velo del fondo: el mismo del Modal (`MODAL_VELO_CLS`). */
export const DIALOGO_VELO = MODAL_VELO_CLS;

/** Panel: radio de diálogo, superficie de tarjeta y borde de 1 px, sin sombra (UI Kit 2026). El ancho lo pone cada diálogo. */
export const DIALOGO_PANEL =
  'relative overflow-clip rounded-[var(--di-radius-modal)] border border-[color:var(--di-border)] bg-card text-flit-primary shadow-none dark:text-foreground';

/** Cabecera del kit (`MODAL_CABECERA_CLS`): ícono opcional de 52 px, título y bajada centrados; la X va absoluta. */
export const DIALOGO_FRANJA = `${MODAL_CABECERA_CLS} text-[color:var(--di-heading)]`;

/** Círculo decorativo del diseño anterior: el kit no lo trae; el `<span aria-hidden>` queda oculto. */
export const DIALOGO_FRANJA_CIRCULO =
  'hidden';

/** Título dentro de la franja (`MODAL_TITULO_CLS`). */
export const DIALOGO_FRANJA_TITULO = `min-w-0 ${MODAL_TITULO_CLS}`;

/** Ícono sobre el título: el disco del kit de 52 px (fondo --kit-hover, glifo azul), como el `icono` del Modal. */
export const DIALOGO_FRANJA_ICONO =
  'h-[52px] w-[52px] shrink-0 rounded-full bg-[var(--kit-hover)] p-3.5 text-[color:var(--kit-azul)]';

/** Botón cerrar (X): arriba a la derecha de la cabecera (`MODAL_CERRAR_CLS`). */
export const DIALOGO_FRANJA_CERRAR = MODAL_CERRAR_CLS;

/** Pie separado por la línea del Modal. */
export const DIALOGO_PIE = 'border-t border-[color:var(--di-divider)]';
