"use client";

import { ArrowLeft } from "lucide-react";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { MandatosCatalogPanel } from "@/components/admin/plataforma/MandatosCatalogPanel";
import { ToastProvider } from "@/components/admin/Toast";

/**
 * SuperAdmin — Plataforma → Mandatos.
 * Visualiza las plantillas de contrato de mandato aplicadas en el proyecto
 * (genérico, Sabaneta, Bello) y su mapeo por OT. Solo SuperAdmin.
 */
export default function AdminMandatosPage() {
  const router = useRouter();

  return (
    <ToastProvider>
      <div className="flex min-h-screen flex-col gap-4 px-4 pt-6 pb-10 md:px-6">
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
          title="Mandatos"
          subtitle="Plantillas de Contrato Privado de Mandato aplicadas por organismo de tránsito. Solo SuperAdmin · lectura."
        />

        <div className="flex flex-1 flex-col rounded-2xl border border-[#DFE5ED] bg-white/60 p-4 dark:border-white/10 dark:bg-[#0B0F14]/60">
          <MandatosCatalogPanel />
        </div>
      </div>
    </ToastProvider>
  );
}
