"use client";

import { X } from "lucide-react";
import type { ReactNode } from "react";

/** Panel lateral con overlay — patrón FLIT admin (drawer AC2 webhooks). */
export interface OtSidePanelProps {
  open: boolean;
  title: string;
  ariaLabel: string;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
  disabled?: boolean;
  /** Clase de z-index del overlay (p. ej. cuando hay otro modal encima). */
  zClassName?: string;
  /**
   * Ancho del drawer. `lg` para paneles densos; `xl` / `2xl` para fichas con grid (p. ej. RL).
   */
  width?: "md" | "lg" | "xl" | "2xl";
  /**
   * Superficie del panel. `modal` usa el fondo claro del prototipo FLIT (`#EEF5FF`).
   */
  surface?: "card" | "modal";
  /**
   * Scroll vertical del cuerpo. `false` para paneles que maquetan su propio alto (p. ej. la guía
   * informativa de documentos, que reparte el contenido en una grilla sin desbordar).
   */
  scrollable?: boolean;
}

export function OtSidePanel({
  open,
  title,
  ariaLabel,
  onClose,
  children,
  footer,
  disabled = false,
  zClassName = "z-50",
  width = "md",
  surface = "card",
  scrollable = true,
}: OtSidePanelProps) {
  if (!open) {
    return null;
  }

  const maxW =
    width === "2xl"
      ? "max-w-4xl"
      : width === "xl"
        ? "max-w-3xl"
        : width === "lg"
          ? "max-w-lg"
          : "max-w-md";

  const surfaceStyle =
    surface === "modal" ? { background: "#EEF5FF" } : undefined;

  return (
    <div className={`fixed inset-0 ${zClassName} flex justify-end`}>
      <button
        type="button"
        className="absolute inset-0 bg-slate-900/40 backdrop-blur-sm"
        aria-label="Cerrar panel"
        onClick={onClose}
        disabled={disabled}
      />
      <aside
        className={`relative flex h-full w-full ${maxW} flex-col border-l shadow-2xl ${surface === "card" ? "bg-card" : ""}`}
        style={surfaceStyle}
        role="dialog"
        aria-modal="true"
        aria-label={ariaLabel}
      >
        <div
          className="flex items-center justify-between border-b px-4 py-3"
        >
          <h2 className="text-sm font-bold text-foreground">
            {title}
          </h2>
          <button type="button" aria-label="Cerrar" onClick={onClose} disabled={disabled}>
            <X className="h-4 w-4" />
          </button>
        </div>
        <div className={`flex-1 p-4 ${scrollable ? "overflow-y-auto" : "min-h-0 overflow-hidden"}`}>
          {children}
        </div>
        {footer && (
          <div className="border-t p-4">
            {footer}
          </div>
        )}
      </aside>
    </div>
  );
}
