"use client";

import { ArrowLeft } from "lucide-react";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { ToastProvider } from "@/components/admin/Toast";
import { TransitOfficesList } from "@/components/admin/transit-offices/TransitOfficesList";

// Consola OT — listado de organismos de tránsito (HU #10236 AC1).
export default function AdminTransitOfficesPage() {
  return (
    <ToastProvider>
      <AdminTransitOfficesPageInner />
    </ToastProvider>
  );
}

function AdminTransitOfficesPageInner() {
  const router = useRouter();

  return (
    <div className="flex min-h-full flex-col gap-4 px-6 pt-6 pb-24">
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
        title="Administración de organismos de tránsito"
        subtitle="Selecciona un OT para configurar trámites, integraciones, reglas y documentos."
      />

      <div
        className="flex flex-1 flex-col rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60"
        style={{ borderColor: "#DFE5ED" }}
      >
        <TransitOfficesList />
      </div>
    </div>
  );
}
