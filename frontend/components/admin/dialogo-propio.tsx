import type { ReactNode } from "react";
import { cn } from "@flit/ui/cn";

/**
 * Homologación (HU #14510, regla 3 de «Homologación» en docs/design/rediseno-ui/REGLAS.md): los diálogos propios de
 * Administración se repintan en su sitio con el aspecto de `@flit/ui/Modal` sin migrarlos, así que conservan su
 * componente, su foco, su tecla Escape y su clic afuera. Estas piezas son solo visuales: copian la franja tinta, el
 * radio, la sombra y el velo del Modal para que todos los diálogos propios se vean igual.
 */

/** Velo detrás del diálogo: el mismo token y desenfoque del Modal. */
export const DIALOGO_VELO_CLS = "bg-[var(--overlay)] backdrop-blur-md";

/** Panel del diálogo: radio de 24 px, superficie de tarjeta y sombra de modal. Quien no tenga scroll propio suma
 * `overflow-hidden` para que la franja respete el radio. */
export const DIALOGO_PANEL_CLS =
  "rounded-[var(--di-radius-modal)] border-0 bg-card text-flit-primary shadow-[var(--shadow-modal)] dark:text-foreground";

/** Título dentro de la franja: el color sobre acento, igual que el `<h2>` del Modal. */
export const DIALOGO_TITULO_CLS = "text-[color:var(--di-on-accent)] dark:text-[color:var(--di-on-accent)]";

/** La X dentro de la franja: círculo translúcido sobre la tinta, como la del Modal. */
export const DIALOGO_CERRAR_CLS =
  "relative z-[1] grid h-10 w-10 shrink-0 place-items-center rounded-full bg-[color-mix(in_srgb,var(--di-on-accent)_18%,transparent)] " +
  "text-[color:var(--di-on-accent)] transition hover:bg-[color-mix(in_srgb,var(--di-on-accent)_30%,transparent)] " +
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--di-on-accent)]";

/**
 * Franja tinta del `.modal__franja` del prototipo, con el círculo turquesa decorativo arriba a la derecha. Recibe el
 * título (y la X, si el diálogo la tiene) tal cual estaban; `className` lleva los márgenes negativos que la pegan al
 * borde del panel cuando el panel tiene padding.
 */
export function FranjaDialogo({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div
      className={cn(
        "relative flex shrink-0 items-center gap-3 overflow-clip bg-[color:var(--di-accent)] px-5 py-4 text-[color:var(--di-on-accent)] sm:px-6 sm:py-5",
        className,
      )}
    >
      <span
        aria-hidden="true"
        className="pointer-events-none absolute -right-[30px] -top-[60px] h-[120px] w-[120px] rounded-full bg-[color:var(--di-highlight-decor)]"
      />
      <div className="relative z-[1] flex min-w-0 flex-1 items-center justify-between gap-3">{children}</div>
    </div>
  );
}
