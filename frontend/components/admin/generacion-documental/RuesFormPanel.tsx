"use client";

import { FileText } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";

export interface RuesFormPanelProps {
  /**
   * Estado de la vista (CF-22). El panel nace en «vacío»: sin hijos no hay nada que emitir.
   * La página de la pestaña lo monta en «lleno» con `RuesGeneracionForm` dentro, que es quien
   * consulta `previewRuesCompany` / `generateRuesDocument` y gobierna sus propios errores.
   */
  status?: UiStatus;
  onRetry?: () => void;
  children?: React.ReactNode;
}

/**
 * Panel de la pestaña "Certificado RUES" (HU-01, CF-01/CF-22).
 *
 * Contenedor de la pestaña: decide cómo se ve vacía, cargando o en error, no qué campos
 * existen. La captura del NIT, la revisión previa y la generación las aporta
 * `RuesGeneracionForm` como hijo, igual que `TransferenciaFormPanel` dentro de
 * `TransferenciaPanel`. De esta HU son los cuatro estados de UI, el encabezado accesible y la
 * advertencia de que la generación no devuelve el PDF en línea (se descarga desde el historial).
 */
export function RuesFormPanel({ status = "empty", onRetry, children }: RuesFormPanelProps) {
  return (
    <section aria-labelledby="gd-rues-title" className="flex flex-1 flex-col gap-3">
      <header className="flex items-start gap-2">
        <FileText className="mt-0.5 h-4 w-4 shrink-0" style={{ color: "#557EFF" }} aria-hidden="true" />
        <div>
          <h2 id="gd-rues-title" className="text-sm font-semibold" style={{ color: "#162744" }}>
            Certificado RUES
          </h2>
          <p className="mt-1 text-xs opacity-70">
            Emite el Certificado RUES por NIT sin abrir un trámite. El documento se genera en el
            servidor y se descarga después desde el historial.
          </p>
        </div>
      </header>

      <UiStateBoundary
        status={status}
        onRetry={onRetry}
        skeletonRows={4}
        emptyMessage="Todavía no hay una consulta RUES en curso en esta pestaña."
        errorMessage="No se pudo consultar el RUES. Intenta nuevamente en unos minutos."
      >
        {children}
      </UiStateBoundary>
    </section>
  );
}
