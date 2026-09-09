"use client";

import { ArrowLeft } from "lucide-react";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { GeneracionDocumentalTabs } from "@/components/admin/generacion-documental/GeneracionDocumentalTabs";
import { HistorialSection } from "@/components/admin/generacion-documental/HistorialSection";

// Pestaña "Historial" del módulo (HU-01, CF-01/CF-22). Los filtros y la redescarga
// presignada auditada son HU-03; aquí queda el listado con sus cuatro estados de UI.
export default function AdminGeneracionDocumentalHistorialPage() {
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
        title="Historial de documentos"
        subtitle="Consulta los documentos generados por tu compañía: tipo, escenario, usuario, fecha y resultado."
      />

      <div className="flex flex-1 flex-col rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60">
        <GeneracionDocumentalTabs activeId="historial" />
        <div className="mt-4">
          <HistorialSection />
        </div>
      </div>
    </div>
  );
}
