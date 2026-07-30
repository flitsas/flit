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
   * Ancho del drawer. `lg` para paneles de detalle densos (trámites clientes).
   */
  width?: "md" | "lg";
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
}: OtSidePanelProps) {
  if (!open) {
    return null;
  }

  const maxW = width === "lg" ? "max-w-lg" : "max-w-md";

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
        className={`relative flex h-full w-full ${maxW} flex-col border-l bg-card shadow-2xl`}
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
        <div className="flex-1 overflow-y-auto p-4">{children}</div>
        {footer && (
          <div className="border-t p-4">
            {footer}
          </div>
        )}
      </aside>
    </div>
  );
}
