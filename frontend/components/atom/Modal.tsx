"use client";

import { useEffect, useId } from "react";
import { X, type LucideIcon } from "lucide-react";

// HU #10496 — Componente Modal unificado (ancho estándar, blur consistente y
// contraste legible en dark). Encapsula el overlay (`fixed inset-0` + backdrop
// blur), el panel (ancho estándar, bordes por token, `dark:bg-[#0B0F14]` y color
// de texto base legible en dark) y el encabezado (icono opcional + título + botón
// cerrar). Reemplaza el boilerplate que cada diálogo repetía con z-index, blur y
// anchos divergentes. El cuerpo (formulario, campos, botones) se pasa como
// `children`; el título lo renderiza el propio Modal para garantizar el contraste.

export type ModalSize = "sm" | "md" | "lg";

// Anchos como clases literales (Tailwind JIT necesita el nombre completo).
// AC1: ancho estándar, "no angosto" → por defecto `md` (max-w-lg).
const SIZE_CLASS: Record<ModalSize, string> = {
  sm: "max-w-md",
  md: "max-w-lg",
  lg: "max-w-2xl",
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
  /** Clase del título; por defecto navy/blanco legible en ambos temas. */
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
  children: React.ReactNode;
}

export function Modal({
  open,
  onClose,
  title,
  icon: Icon,
  iconBg = "#557EFF",
  titleClassName = "text-base font-bold text-[#162744] dark:text-white",
  description,
  size = "md",
  zClassName = "z-[100]",
  busy = false,
  closeOnBackdrop = true,
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

  if (!open) return null;

  const requestClose = () => {
    if (!busy) onClose();
  };

  return (
    <div
      className={`fixed inset-0 ${zClassName} flex items-center justify-center bg-slate-900/50 px-4 backdrop-blur-md`}
      role="dialog"
      aria-modal="true"
      aria-labelledby={titleId}
      onMouseDown={(e) => {
        if (closeOnBackdrop && e.target === e.currentTarget) requestClose();
      }}
    >
      <div
        className={`w-full ${SIZE_CLASS[size]} rounded-2xl border border-[#DFE5ED] bg-white p-6 text-[#162744] shadow-2xl dark:border-white/10 dark:bg-[#0B0F14] dark:text-white`}
      >
        <div className="mb-4 flex items-start justify-between gap-3">
          <div className="flex min-w-0 items-center gap-2">
            {Icon && (
              <span
                className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl"
                style={{ background: iconBg }}
              >
                <Icon className="h-4.5 w-4.5 text-white" />
              </span>
            )}
            <div className="min-w-0">
              <h2 id={titleId} className={titleClassName}>
                {title}
              </h2>
              {description && (
                <p className="mt-0.5 text-xs opacity-70">{description}</p>
              )}
            </div>
          </div>
          <button
            type="button"
            aria-label="Cerrar"
            onClick={requestClose}
            className="shrink-0 text-slate-400 hover:text-slate-700 dark:hover:text-white"
          >
            <X className="h-5 w-5" />
          </button>
        </div>
        {children}
      </div>
    </div>
  );
}
