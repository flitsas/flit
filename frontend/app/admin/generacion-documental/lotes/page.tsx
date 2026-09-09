"use client";

import { ArrowLeft } from "lucide-react";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { GeneracionDocumentalTabs } from "@/components/admin/generacion-documental/GeneracionDocumentalTabs";
import { LoteCargaPanel } from "@/components/admin/generacion-documental/LoteCargaPanel";

// Pestaña "Carga masiva" del módulo (HU #12224, CF-11/CF-12/CF-16).
//
// Es la entrada que le faltaba al incremento I3: el backend de lotes ya estaba entero —plantilla,
// parser, worker, idempotencia y ZIP— pero no había forma de llegar a él desde la aplicación. El
// seguimiento de un lote concreto cuelga de aquí, en `lotes/[batchId]`.
//
// El acceso al módulo lo resuelve el layout por módulos accesibles; el permiso de generar lo
// verifica el backend en `POST /lotes`.
export default function AdminGeneracionDocumentalLotesPage() {
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
        <GeneracionDocumentalTabs activeId="lotes" />
        <div className="mt-4">
          <LoteCargaPanel />
        </div>
      </div>
    </div>
  );
}
