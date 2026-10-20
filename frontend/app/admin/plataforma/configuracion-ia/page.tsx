"use client";

import { ArrowLeft } from "lucide-react";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { ConfiguracionIaPanel } from "@/components/admin/plataforma/ai/ConfiguracionIaPanel";
import { ToastProvider } from "@/components/admin/Toast";

/**
 * SuperAdmin — Plataforma → Configuración de IA (Feature #13508, HU #13511/#13512).
 * Multi-proveedor por herramienta (OCR / DR. FLIT): prioridad, activo, historial y alertas.
 * Solo SuperAdmin (menú oculto y URL directa → /403 para el resto).
 */
export default function AdminConfiguracionIaPage() {
  const router = useRouter();

  return (
    <ToastProvider>
      <div className="flex min-h-screen flex-col gap-4 px-4 pt-6 pb-10 md:px-6">
        <button
          type="button"
          onClick={() => router.push("/")}
          className="flex w-fit items-center gap-1.5 rounded-md text-xs font-semibold text-flit-brand focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand"
        >
          <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
          Volver al inicio
        </button>

        <ModuleTitle
          title="Configuración de IA"
          subtitle="Proveedores de OCR y DR. FLIT: prioridad, activo, historial de cambios y alertas de failover. Solo SuperAdmin · secretos solo en el entorno."
        />

        <div className="flex flex-1 flex-col rounded-2xl border border-[#DFE5ED] bg-white/60 p-4 dark:border-white/10 dark:bg-[#0B0F14]/60">
          <ConfiguracionIaPanel />
        </div>
      </div>
    </ToastProvider>
  );
}
