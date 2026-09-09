"use client";

import { FileSignature } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";

export interface TransferenciaPanelProps {
  /** Estado de la vista (CF-22). Ver la nota de `RuesFormPanel`. */
  status?: UiStatus;
  onRetry?: () => void;
  children?: React.ReactNode;
}

/**
 * Panel de la pestaña "Transferencia" (HU-01, CF-01/CF-22).
 *
 * Contenedor de la pestaña con sus cuatro estados de UI (CF-22). El contenido —control de
 * régimen aplicable (CF-24), selector de escenario A/B/C y formulario— lo aporta
 * `TransferenciaFormPanel` como hijo: este panel decide cómo se ve la pestaña vacía, cargando
 * o en error, no qué campos existen. El prellenado «placa primero» llega en HU-10.
 */
export function TransferenciaPanel({ status = "empty", onRetry, children }: TransferenciaPanelProps) {
  return (
    <section aria-labelledby="gd-transferencia-title" className="flex flex-1 flex-col gap-3">
      <header className="flex items-start gap-2">
        <FileSignature className="mt-0.5 h-4 w-4 shrink-0" style={{ color: "#557EFF" }} aria-hidden="true" />
        <div>
          <h2 id="gd-transferencia-title" className="text-sm font-semibold" style={{ color: "#162744" }}>
            Transferencia de dominio
          </h2>
          <p className="mt-1 text-xs opacity-70">
            Emite el documento privado de transferencia de dominio sin abrir un trámite.
          </p>
        </div>
      </header>

      <UiStateBoundary
        status={status}
        onRetry={onRetry}
        skeletonRows={4}
        emptyMessage="El formulario de transferencia aún no está habilitado en este ambiente."
        errorMessage="No se pudo cargar el formulario de transferencia. Intenta nuevamente."
      >
        {children}
      </UiStateBoundary>
    </section>
  );
}
