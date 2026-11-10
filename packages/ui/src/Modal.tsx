"use client";

import { useEffect, useId } from "react";
import { createPortal } from "react-dom";
import { X, type LucideIcon } from "lucide-react";
import { cn } from "./cn";

// HU #10496 — Componente Modal unificado (ancho estándar, blur consistente y
// contraste legible en dark). Encapsula el overlay (`fixed inset-0` + backdrop
// blur), el panel (ancho estándar, superficie y bordes por token, tema oscuro incluido, y color
// de texto base legible en dark) y el encabezado (icono opcional + título + botón
// cerrar). Reemplaza el boilerplate que cada diálogo repetía con z-index, blur y
// anchos divergentes. El cuerpo (formulario, campos, botones) se pasa como
// `children`; el título lo renderiza el propio Modal para garantizar el contraste.
// HU #14476 — aspecto del `.modal` de la propuesta: radio de 24 px, franja tinta arriba con el ícono, el título y la X
// en el color sobre acento, círculo turquesa decorativo y pie separado por una línea. Mismas props.

export type ModalSize = "sm" | "md" | "lg" | "xl" | "2xl";

// Anchos como clases literales (Tailwind JIT necesita el nombre completo).
// AC1: ancho estándar, "no angosto" → por defecto `md` (max-w-lg).
// `xl` (max-w-4xl) para contenido ancho como tablas (p. ej. documentos del expediente OT).
// `2xl` (max-w-6xl) — Frente C, etapa 1: el modal de detalle del trámite (cabecera + tarjeta de
// vehículo + trazabilidad a dos columnas) necesita más ancho que `xl`. Aditivo: ninguna pantalla
// existente cambia.
const SIZE_CLASS: Record<ModalSize, string> = {
  sm: "max-w-md",
  md: "max-w-lg",
  lg: "max-w-2xl",
  xl: "max-w-4xl",
  "2xl": "max-w-6xl",
};

export interface ModalProps {
  /** Controla el montaje. Si es false, no renderiza nada. */
  open: boolean;
  /** Cierre solicitado (X, Escape o clic en el backdrop). No se dispara si `busy`. */
  onClose: () => void;
  /** Título accesible; el Modal lo asocia vía `aria-labelledby`. */
  title: string;
  /** Icono opcional a la izquierda del título (Lucide). */
  icon?: LucideIcon;
  /** Fondo del recuadro del icono (por defecto azul de marca). */
  iconBg?: string;
  /** Clase del título (tamaño y peso). El color lo fija la franja (`--di-on-accent`) para que siempre se lea. */
  titleClassName?: string;
  /** Subtítulo opcional bajo el título. */
  description?: React.ReactNode;
  /** Ancho estándar del panel. `md` (max-w-lg) por defecto. */
  size?: ModalSize;
  /** Clase de z-index (literal para JIT). Por defecto `z-[100]`. */
  zClassName?: string;
  /** Si está ocupado, el modal no se puede cerrar (X/Escape/backdrop quedan inertes). */
  busy?: boolean;
  /** Permite cerrar al hacer clic fuera del panel. `true` por defecto. */
  closeOnBackdrop?: boolean;
  /** Reemplaza el encabezado por defecto. Puede ser nodo o función que recibe `titleId` para a11y. */
  header?: React.ReactNode | ((ctx: { titleId: string }) => React.ReactNode);
  /** Clases adicionales en el panel (ancho sigue gobernado por `size`). */
  panelClassName?: string;
  /** Clases adicionales en el contenedor scroll del cuerpo. */
  bodyClassName?: string;
  /** Pie fijo (fuera del scroll). Evita que un CTA al final dispare barras horizontales. */
  footer?: React.ReactNode;
  children: React.ReactNode;
}

export function Modal({
  open,
  onClose,
  title,
  icon: Icon,
  iconBg = "var(--color-flit-brand)",
  titleClassName = "text-base font-semibold",
  description,
  size = "md",
  zClassName = "z-[100]",
  busy = false,
  closeOnBackdrop = true,
  header,
  panelClassName = "",
  bodyClassName = "",
  footer,
  children,
}: ModalProps) {
  const titleId = useId();

  // Escape cierra el modal (salvo que esté ocupado).
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape" && !busy) onClose();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open, busy, onClose]);

  // Se renderiza vía portal a <body> (guardado para SSR): un modal anidado dentro de otro cuyo
  // overlay usa `backdrop-blur` heredaría ese containing block y su `fixed inset-0` quedaría
  // recortado al recuadro del modal padre (fondo oscuro confinado a una caja). Portalizar a body
  // hace que el overlay cubra el viewport real, sin importar los ancestros. Se portaliza de forma
  // síncrona (sin diferir el montaje) para no romper el auto-foco que los hijos hacen en su efecto
  // de montaje.
  if (!open || typeof document === "undefined") return null;

  const requestClose = () => {
    if (!busy) onClose();
  };

  const resolvedHeader =
    header === undefined
      ? undefined
      : typeof header === 'function'
        ? header({ titleId })
        : header;

  return createPortal(
    <div
      className={`fixed inset-0 ${zClassName} flex items-center justify-center overflow-hidden bg-[var(--overlay)] px-4 py-6 backdrop-blur-md`}
      role="dialog"
      aria-modal="true"
      aria-labelledby={titleId}
      onMouseDown={(e) => {
        if (closeOnBackdrop && e.target === e.currentTarget) requestClose();
      }}
      // El portal saca este árbol del DOM del contenedor (p. ej. el `<tr onClick={...}>` de una
      // fila de tabla), pero React sigue burbujeando el evento sintético por el árbol de React, no
      // el del DOM. Sin este corte, cerrar con la X o con clic en el backdrop reactiva el onClick
      // del ancestro (por ejemplo, abre el trámite de la fila que montó el modal).
      onClick={(e) => e.stopPropagation()}
    >
      <div
        className={`relative flex max-h-[calc(100dvh-3rem)] w-full min-w-0 ${SIZE_CLASS[size]} flex-col overflow-clip rounded-[var(--di-radius-modal)] bg-[color:var(--di-surface-elevated)] text-[color:var(--di-text-primary)] shadow-[var(--shadow-modal)] ${panelClassName}`}
      >
        {resolvedHeader !== undefined ? (
          <div className="shrink-0 px-4 pt-4 sm:px-6 sm:pt-6">{resolvedHeader}</div>
        ) : (
          // Franja tinta del `.modal__franja` del prototipo: ícono y título en el color sobre acento, y un círculo
          // turquesa decorativo arriba a la derecha sobre el que queda la X.
          <div className="relative flex shrink-0 items-center gap-3 overflow-clip bg-[color:var(--di-accent)] px-5 py-4 text-[color:var(--di-on-accent)] sm:px-6 sm:py-5">
            <span
              aria-hidden="true"
              className="pointer-events-none absolute -right-[30px] -top-[60px] h-[120px] w-[120px] rounded-full bg-[color:var(--di-highlight-decor)]"
            />
            {Icon && (
              <span
                className="relative z-[1] flex h-9 w-9 shrink-0 items-center justify-center rounded-xl"
                style={{ background: iconBg }}
              >
                <Icon className="h-4.5 w-4.5 text-white" />
              </span>
            )}
            <div className="relative z-[1] min-w-0 flex-1 leading-tight">
              <h2
                id={titleId}
                className={cn(titleClassName, "text-[color:var(--di-on-accent)] dark:text-[color:var(--di-on-accent)]")}
              >
                {title}
              </h2>
              {description && <p className="mt-0.5 text-xs opacity-85">{description}</p>}
            </div>
            <button
              type="button"
              aria-label="Cerrar"
              onClick={requestClose}
              className="relative z-[1] grid h-10 w-10 shrink-0 place-items-center rounded-full bg-[color-mix(in_srgb,var(--di-on-accent)_18%,transparent)] text-[color:var(--di-on-accent)] transition hover:bg-[color-mix(in_srgb,var(--di-on-accent)_30%,transparent)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--di-on-accent)]"
            >
              <X className="h-5 w-5" />
            </button>
          </div>
        )}
        <div className="flex min-h-0 flex-1 flex-col p-4 sm:p-6">
          <div className={`min-h-0 flex-1 overflow-x-hidden overflow-y-auto ${bodyClassName}`}>{children}</div>
          {footer ? (
            <div className="mt-4 shrink-0 border-t border-[color:var(--di-divider)] pt-4">{footer}</div>
          ) : null}
        </div>
      </div>
    </div>,
    document.body,
  );
}
