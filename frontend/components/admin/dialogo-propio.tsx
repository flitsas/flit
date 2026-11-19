import type { ReactNode } from "react";
import { cn } from "@flit/ui/cn";
import { Icono, type NombreIcono } from "@flit/ui/Icono";

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
  "absolute right-2 top-2 z-[1] grid h-9 w-9 shrink-0 place-items-center rounded-full bg-transparent " +
  "text-[color:var(--di-text-tertiary)] transition hover:bg-[var(--kit-hover)] hover:text-[color:var(--di-action)] " +
  "focus-visible:outline-2 focus-visible:outline-[var(--di-action)]";

/**
 * Cabecera del kit (`.modal__franja` de la propuesta-2) para los diálogos propios: el ícono opcional de 52 px en su disco,
 * el título y la bajada centrados, y la X (si viene como hijo con DIALOGO_CERRAR_CLS) flotando arriba a la derecha. Recibe
 * el título (y la X) tal cual estaban; `className` lleva los márgenes que la pegan al borde del panel si el panel tiene
 * padding. Foco, Escape y manejadores no cambian.
 */
export function FranjaDialogo({ children, className, icono }: { children: ReactNode; className?: string; icono?: NombreIcono }) {
  return (
    <div
      className={cn(
        "relative flex shrink-0 flex-col items-center gap-2 bg-transparent px-14 pb-1 pt-6 text-center text-[color:var(--di-heading)] " +
          "[&_h2]:text-[1.375rem] [&_h2]:font-bold [&_h2]:leading-tight [&_h3]:text-[1.375rem] [&_h3]:font-bold [&_h3]:leading-tight",
        className,
      )}
    >
      {icono ? <Icono nombre={icono} disco size={52} /> : null}
      <div className="flex min-w-0 flex-col items-center gap-1 [&>:first-child]:min-w-0">{children}</div>
    </div>
  );
}
