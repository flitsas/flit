"use client";

import { ArrowLeft } from "lucide-react";
import { useParams, useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { GeneracionDocumentalTabs } from "@/components/admin/generacion-documental/GeneracionDocumentalTabs";
import { BatchProgressPanel } from "@/components/admin/generacion-documental/BatchProgressPanel";
import { generacionDocumentalTabPath } from "@/components/admin/generacion-documental/generacion-documental-nav";

/**
 * Seguimiento de un lote XLSX (HU #12211; CF-13, CF-14, CF-15).
 *
 * <p>Cuelga del módulo pero NO añade una cuarta pestaña: las pestañas del módulo son las tres
 * de CF-01 (Certificado RUES, Transferencia e Historial) y esta vista es un detalle al que se
 * llega desde el historial filtrado por lote. La barra se pinta con «Historial» activo porque
 * de ahí se viene y ahí se vuelve.</p>
 *
 * <p>El acceso ya lo resolvió el layout del módulo por módulos accesibles; el aislamiento por
 * compañía lo resuelve el backend con un 404 escueto, que el panel traduce a «no encontramos ese
 * lote» sin revelar si existe en otra compañía.</p>
 */
export default function AdminGeneracionDocumentalLoteDetallePage() {
  const router = useRouter();
  const params = useParams<{ batchId: string }>();
  const batchId = typeof params?.batchId === "string" ? params.batchId : "";

  return (
    <div className="flex min-h-screen flex-col gap-4 px-4 md:px-6 pt-6 pb-10">
      <button
        type="button"
        onClick={() => router.push(generacionDocumentalTabPath("historial"))}
        className="flex w-fit items-center gap-1.5 text-xs font-semibold"
        style={{ color: "#557EFF" }}
      >
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
        Volver al historial
      </button>

      <ModuleTitle
        title="Seguimiento del lote"
        subtitle="Avance en vivo de la carga masiva, detalle de cada fila y descarga de los documentos que sí salieron."
      />

      <div className="flex flex-1 flex-col rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60">
        <GeneracionDocumentalTabs activeId="historial" />
        <div className="mt-4">
          {batchId ? (
            <BatchProgressPanel batchId={batchId} />
          ) : (
            <p role="alert" className="text-xs opacity-80">
              No se indicó ningún lote para consultar.
            </p>
          )}
        </div>
      </div>
    </div>
  );
}
