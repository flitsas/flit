"use client";

import type { ReactNode } from "react";
import { AlertTriangle, Inbox, RefreshCw } from "lucide-react";
import { cn } from "@/lib/utils";

// Wrapper reutilizable de los 4 estados UI (HU #10194, AC7): cargando (skeleton),
// vacío (con CTA opcional), error (con reintentar) y contenido lleno.
export type UiStatus = "loading" | "empty" | "error" | "ready";

export interface UiStateBoundaryProps {
  status: UiStatus;
  children: ReactNode;
  /** Texto del estado vacío. */
  emptyMessage?: string;
  /** CTA opcional del estado vacío. */
  emptyCta?: ReactNode;
  /** Mensaje del estado de error. */
  errorMessage?: string;
  /** Handler de reintento (estado error). */
  onRetry?: () => void;
  /** Número de filas del skeleton de carga. */
  skeletonRows?: number;
  className?: string;
}

export function UiStateBoundary({
  status,
  children,
  emptyMessage = "No hay información para mostrar.",
  emptyCta,
  errorMessage = "Ocurrió un error al cargar la información.",
  onRetry,
  skeletonRows = 4,
  className,
}: UiStateBoundaryProps) {
  if (status === "loading") {
    return (
      <div
        className={cn("space-y-2", className)}
        role="status"
        aria-busy="true"
        aria-live="polite"
        data-testid="ui-loading"
      >
        <span className="sr-only">Cargando…</span>
        {Array.from({ length: skeletonRows }).map((_, i) => (
          <div
            key={i}
            className="h-12 rounded-xl border animate-pulse bg-[#F4F7FC]"
            style={{ borderColor: "#DFE5ED" }}
          />
        ))}
      </div>
    );
  }

  if (status === "error") {
    return (
      <div
        className={cn(
          "flex flex-col items-center justify-center gap-3 rounded-2xl border p-8 text-center",
          className,
        )}
        style={{ borderColor: "#DFE5ED" }}
        role="alert"
        data-testid="ui-error"
      >
        <AlertTriangle className="h-8 w-8" style={{ color: "#FF4E00" }} />
        <p className="text-sm font-medium">{errorMessage}</p>
        {onRetry && (
          <button
            type="button"
            onClick={onRetry}
            className="flex items-center gap-2 rounded-xl px-4 py-2 text-xs font-semibold text-white"
            style={{ background: "#557EFF" }}
          >
            <RefreshCw className="h-3.5 w-3.5" /> Reintentar
          </button>
        )}
      </div>
    );
  }

  if (status === "empty") {
    return (
      <div
        className={cn(
          "flex flex-col items-center justify-center gap-3 rounded-2xl border p-8 text-center",
          className,
        )}
        style={{ borderColor: "#DFE5ED" }}
        data-testid="ui-empty"
      >
        <Inbox className="h-8 w-8 opacity-50" />
        <p className="text-sm opacity-70">{emptyMessage}</p>
        {emptyCta}
      </div>
    );
  }

  return <>{children}</>;
}
