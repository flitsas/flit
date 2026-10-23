"use client";

import { ArrowLeft } from "lucide-react";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { GeneracionDocumentalTabs } from "@/components/admin/generacion-documental/GeneracionDocumentalTabs";
import { TransferenciaPanel } from "@/components/admin/generacion-documental/TransferenciaPanel";
import { TransferenciaFormPanel } from "@/components/admin/generacion-documental/TransferenciaFormPanel";
import { ADMIN_BACK_LINK_CLS, ADMIN_CONTENT_SURFACE_CLS } from "@/components/admin/admin-ui-styles";

// Pestaña "Transferencia" del módulo (HU-01, CF-01). HU #12207 monta el formulario del
// ESCENARIO A dentro del shell; el régimen aplicable (CF-24) y el selector A/B/C son HU-06,
// y el prellenado «placa primero» es HU-10.
export default function AdminGeneracionDocumentalTransferenciaPage() {
  const router = useRouter();

  return (
    <div className="flex min-h-screen flex-col gap-4 px-4 md:px-6 pt-6 pb-10">
      <button type="button" onClick={() => router.push("/")} className={ADMIN_BACK_LINK_CLS}>
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
        Volver al inicio
      </button>

      <ModuleTitle
        title="Transferencia de dominio"
        subtitle="Emite el documento privado de transferencia de dominio del vehículo sin abrir un trámite."
      />

      <div className={ADMIN_CONTENT_SURFACE_CLS}>
        <GeneracionDocumentalTabs activeId="transferencia" />
        <div className="mt-4">
          <TransferenciaPanel status="ready">
            <TransferenciaFormPanel />
          </TransferenciaPanel>
        </div>
      </div>
    </div>
  );
}
