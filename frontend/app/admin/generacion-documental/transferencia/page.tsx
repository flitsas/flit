"use client";

import { ArrowLeft } from "lucide-react";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { GeneracionDocumentalTabs } from "@/components/admin/generacion-documental/GeneracionDocumentalTabs";
import { TransferenciaPanel } from "@/components/admin/generacion-documental/TransferenciaPanel";

// Pestaña "Transferencia" del módulo (HU-01, CF-01). Shell: el régimen aplicable (CF-24),
// el escenario A/B/C y el formulario son HU-05/HU-06/HU-10.
export default function AdminGeneracionDocumentalTransferenciaPage() {
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
        title="Transferencia de dominio"
        subtitle="Emite el documento privado de transferencia de dominio del vehículo sin abrir un trámite."
      />

      <div className="flex flex-1 flex-col rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60">
        <GeneracionDocumentalTabs activeId="transferencia" />
        <div className="mt-4">
          <TransferenciaPanel />
        </div>
      </div>
    </div>
  );
}
