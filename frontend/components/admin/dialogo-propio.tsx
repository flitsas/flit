import type { ReactNode } from "react";
import { cn } from "@flit/ui/cn";

/**
 * Los diálogos propios de Administración se repintan en su sitio con el aspecto de `@flit/ui/Modal` sin migrarlos, así
 * que conservan su componente, su foco, su tecla Escape y su clic afuera. UI Kit 2026: panel blanco con borde de 1 px
 * y radio de 9 px, sin sombra; cabecera blanca con el título en azul; velo azul al 50 % con desenfoque de 5 px.
 */

/** Velo detrás del diálogo: el mismo token y desenfoque del Modal. */
export const DIALOGO_VELO_CLS = "bg-[var(--overlay)] backdrop-blur-[5px]";

/** Panel del diálogo: radio de 24 px, superficie de tarjeta y sombra de modal. Quien no tenga scroll propio suma
 * `overflow-hidden` para que la franja respete el radio. */
export const DIALOGO_PANEL_CLS =
  "rounded-[9px] border border-[color:var(--di-border)] bg-card text-flit-primary shadow-none dark:text-foreground";

/** Título dentro de la franja: el color sobre acento, igual que el `<h2>` del Modal. */
export const DIALOGO_TITULO_CLS = "text-[color:var(--di-heading)] dark:text-[color:var(--di-heading)]";

/** La X dentro de la franja: círculo translúcido sobre la tinta, como la del Modal. */
export const DIALOGO_CERRAR_CLS =
  "relative z-[1] grid h-9 w-9 shrink-0 place-items-center rounded-full bg-transparent " +
  "text-[color:var(--di-text-tertiary)] transition hover:bg-[var(--kit-hover)] hover:text-[color:var(--di-action)] " +
  "focus-visible:outline-2 focus-visible:outline-[var(--di-action)]";

/**
 * Franja tinta del `.modal__franja` del prototipo, con el círculo turquesa decorativo arriba a la derecha. Recibe el
 * título (y la X, si el diálogo la tiene) tal cual estaban; `className` lleva los márgenes negativos que la pegan al
 * borde del panel cuando el panel tiene padding.
 */
export function FranjaDialogo({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div
      className={cn(
        "relative flex shrink-0 items-center gap-3 bg-transparent px-5 pb-1 pt-5 text-[color:var(--di-heading)] sm:px-6 sm:pt-6 [&_h2]:text-[1.375rem] [&_h2]:font-bold [&_h3]:text-[1.375rem] [&_h3]:font-bold",
        className,
      )}
    >
      <div className="relative z-[1] flex min-w-0 flex-1 items-center justify-between gap-3 [&>:first-child]:min-w-0">
        {children}
      </div>
    </div>
  );
}
