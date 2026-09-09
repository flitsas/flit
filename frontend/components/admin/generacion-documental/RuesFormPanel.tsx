"use client";

import { FileText } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";

export interface RuesFormPanelProps {
  /**
   * Estado de la vista (CF-22). El shell de HU-01 nace en «vacío»: no hay consulta en
   * curso. HU-02 conecta `previewRuesCompany` / `generateRuesDocument` y hace girar los
   * otros tres estados con datos reales; el contrato de estados ya queda cableado aquí.
   */
  status?: UiStatus;
  onRetry?: () => void;
  children?: React.ReactNode;
}

/**
 * Panel de la pestaña "Certificado RUES" (HU-01, CF-01/CF-22).
 *
 * Cascarón del formulario: la captura del NIT, la revisión previa y la generación son
 * alcance de HU-02/HU-05. Lo que sí es de esta HU y queda resuelto: los cuatro estados de
 * UI (vacío, cargando, error, lleno), el encabezado accesible del panel y la advertencia
 * de que la generación no devuelve el PDF en línea (se descarga desde el historial).
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
