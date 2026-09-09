"use client";

import { ArrowLeft } from "lucide-react";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { GeneracionDocumentalTabs } from "@/components/admin/generacion-documental/GeneracionDocumentalTabs";
import { RuesFormPanel } from "@/components/admin/generacion-documental/RuesFormPanel";

// Landing del módulo "Generación documental" (HU-01, CF-01): pestaña Certificado RUES.
// El formulario y la generación real son HU-02/HU-05; aquí queda el shell con los cuatro
// estados de UI y la navegación entre pestañas sin recarga de página.
export default function AdminGeneracionDocumentalPage() {
  const router = useRouter();

  return (
    <div className="flex min-h-screen flex-col gap-4 px-4 md:px-6 pt-6 pb-10">
      <button
        type="button"
        onClick={() => router.push("/")}
        className="flex w-fit items-center gap-1.5 text-xs font-semibold"
        style={{ color: "#557EFF" }}
      >
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
        Volver al inicio
      </button>

      <ModuleTitle
        title="Generación documental"
        subtitle="Emite Certificados RUES y documentos de transferencia de dominio sin abrir un trámite, y consulta el historial de lo generado."
      />

      <div className="flex flex-1 flex-col rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60">
        <GeneracionDocumentalTabs activeId="rues" />
        <div className="mt-4">
          <RuesFormPanel />
        </div>
      </div>
    </div>
  );
}
