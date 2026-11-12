"use client";

import { useEffect, useId } from "react";
import { createPortal } from "react-dom";
import type { LucideIcon } from "lucide-react";
import { cn } from "./cn";
import { Icono, type NombreIcono } from "./Icono";

// HU #10496 — Componente Modal unificado (ancho estándar, blur consistente y
// contraste legible en dark). Encapsula el overlay (`fixed inset-0` + backdrop
// blur), el panel (ancho estándar, superficie y bordes por token, tema oscuro incluido, y color
// de texto base legible en dark) y el encabezado (icono opcional + título + botón
// cerrar). Reemplaza el boilerplate que cada diálogo repetía con z-index, blur y
// anchos divergentes. El cuerpo (formulario, campos, botones) se pasa como
// `children`; el título lo renderiza el propio Modal para garantizar el contraste.
// UI Kit 2026 (propuesta-2, `.modal`): contenedor blanco de radio 9 px con borde de 1 px y sin sombra; ícono de 52 px
// centrado sobre el título (1,375 rem, azul), subtítulo gris, X discreta arriba a la derecha y velo azul al 50 % con
// desenfoque de 5 px. Mismas props y mismo comportamiento (Escape, clic fuera, ocupado, portal).

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
  /** Ícono del kit centrado sobre el título (preferido). */
  icono?: NombreIcono;
  /** @deprecated Usa `icono`. Ícono Lucide; se pinta en el mismo disco centrado mientras se migra. */
  icon?: LucideIcon;
  /** @deprecated Sin efecto: el disco es el del kit. */
  iconBg?: string;
  /** Clase del título (tamaño y peso). El color es el de los títulos del kit (`--di-heading`). */
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
  icono,
  icon: Icon,
  iconBg,
  titleClassName = "text-[1.375rem] font-bold leading-tight",
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
  void iconBg;

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
      className={`fixed inset-0 ${zClassName} flex items-center justify-center overflow-hidden bg-[var(--overlay)] px-3 py-6 backdrop-blur-[5px]`}
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
        className={`relative flex max-h-[calc(100dvh-3rem)] w-full min-w-0 ${SIZE_CLASS[size]} flex-col overflow-clip rounded-[var(--di-radius-modal)] border border-[color:var(--di-border)] bg-card text-flit-primary shadow-none dark:text-foreground ${panelClassName}`}
      >
        {resolvedHeader !== undefined ? (
          <div className="mx-4 mt-4 shrink-0 sm:mx-6 sm:mt-6">{resolvedHeader}</div>
        ) : (
          // `.modal__franja` del kit: ícono centrado, título y subtítulo; la X flota arriba a la derecha.
          <div className="relative flex shrink-0 flex-col items-center gap-2 px-14 pb-1 pt-6 text-center">
            {icono ? (
              <Icono nombre={icono} disco size={52} />
            ) : Icon ? (
              <span aria-hidden="true" className="kit-disco h-[52px] w-[52px]">
                <Icon className="h-7 w-7" strokeWidth={1.75} />
              </span>
            ) : null}
            <div className="min-w-0 leading-tight">
              <h2 id={titleId} className={cn(titleClassName, "text-[color:var(--di-heading)]")}>
                {title}
              </h2>
              {description && <p className="mt-1 text-sm text-[color:var(--di-text-tertiary)]">{description}</p>}
            </div>
            <button
              type="button"
              aria-label="Cerrar"
              onClick={requestClose}
              className="absolute right-2 top-2 grid h-9 w-9 place-items-center rounded-full bg-transparent text-[color:var(--di-text-tertiary)] transition hover:bg-[color:var(--kit-hover)] hover:text-[color:var(--di-action)] focus-visible:outline-2 focus-visible:outline-[var(--di-action)]"
            >
              <Icono nombre="cerrar" size={20} />
            </button>
          </div>
        )}
        {/* Márgenes y no padding: el cuerpo conserva su barra de scroll y el `px-*` que algunos llamadores le pasan. */}
        <div
          className={`mx-4 mt-4 min-h-0 flex-1 overflow-x-hidden overflow-y-auto sm:mx-6 ${footer ? "" : "mb-4 sm:mb-6"} ${bodyClassName}`}
        >
          {children}
        </div>
        {footer ? (
          <div className="mx-4 mb-4 mt-4 shrink-0 border-t border-[color:var(--di-divider)] pt-4 sm:mx-6 sm:mb-6">
            {footer}
          </div>
        ) : null}
      </div>
    </div>,
    document.body,
  );
}
