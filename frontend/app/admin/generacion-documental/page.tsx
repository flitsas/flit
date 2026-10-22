"use client";

import { ArrowLeft } from "lucide-react";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { GeneracionDocumentalTabs } from "@/components/admin/generacion-documental/GeneracionDocumentalTabs";
import { RuesFormPanel } from "@/components/admin/generacion-documental/RuesFormPanel";
import { RuesGeneracionForm } from "@/components/admin/generacion-documental/RuesGeneracionForm";
import { ADMIN_BACK_LINK_CLS, ADMIN_CONTENT_SURFACE_CLS } from "@/components/admin/admin-ui-styles";

// Landing del módulo "Generación documental" (HU-01, CF-01): pestaña Certificado RUES.
// El shell con los cuatro estados de UI y la navegación entre pestañas es de HU-01; la captura
// del NIT, la revisión previa y la generación las aporta `RuesGeneracionForm`, igual que
// `TransferenciaFormPanel` se monta dentro de `TransferenciaPanel` en la otra pestaña.
export default function AdminGeneracionDocumentalPage() {
  const router = useRouter();

  return (
    <div className="flex min-h-screen flex-col gap-4 px-4 md:px-6 pt-6 pb-10">
      <button type="button" onClick={() => router.push("/")} className={ADMIN_BACK_LINK_CLS}>
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
        Volver al inicio
      </button>

      <ModuleTitle
        title="Generación documental"
        subtitle="Emite Certificados RUES y documentos de transferencia de dominio sin abrir un trámite, y consulta el historial de lo generado."
      />

      <div className={ADMIN_CONTENT_SURFACE_CLS}>
        <GeneracionDocumentalTabs activeId="rues" />
        <div className="mt-4">
          <RuesFormPanel status="ready">
            <RuesGeneracionForm />
          </RuesFormPanel>
        </div>
      </div>
    </div>
  );
}
