"use client";

// HU #12123 — formulario de cadencia ICT. /admin/jobs/ict es SuperAdmin (middleware).
import { ArrowLeft } from "lucide-react";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { ToastProvider } from "@/components/admin/Toast";
import { IctJobSettingsForm } from "@/components/admin/jobs/IctJobSettingsForm";
import { JobsComoFunciona } from "@/components/admin/jobs/JobsComoFunciona";

export default function AdminIctJobSettingsPage() {
  const router = useRouter();

  return (
    <ToastProvider>
      <div className="flex min-h-screen flex-col gap-4 px-4 pt-6 pb-24 md:px-6">
        <button
          type="button"
          onClick={() => router.push("/admin/jobs")}
          className="flex w-fit items-center gap-1.5 text-xs font-semibold"
          style={{ color: "#557EFF" }}
        >
          <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
          Volver al catálogo
        </button>

        <ModuleTitle
          title="Cadencia ICT"
          subtitle="Ventana horaria Bogotá, intervalos, lotes y concurrencia. Los cambios aplican en el siguiente ciclo."
          action={<JobsComoFunciona />}
        />

        <IctJobSettingsForm />
      </div>
    </ToastProvider>
  );
}
