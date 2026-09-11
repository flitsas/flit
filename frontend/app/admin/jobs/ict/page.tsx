"use client";

// HU #12123 — formulario de cadencia ICT. /admin/jobs/ict es SuperAdmin (middleware).
import { ArrowLeft } from "lucide-react";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { ToastProvider } from "@/components/admin/Toast";
import { IctJobSettingsForm } from "@/components/admin/jobs/IctJobSettingsForm";

export default function AdminIctJobSettingsPage() {
  const router = useRouter();

  return (
    <ToastProvider>
      <div className="flex min-h-screen flex-col gap-4 px-6 pt-6 pb-10">
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
          title="Procesos periódicos ICT"
          subtitle="Ventana horaria Bogotá, polls, lotes y concurrencia del pipeline. Solo SuperAdmin. Sin secretos."
        />

        <div className="mx-auto w-full max-w-3xl">
          <IctJobSettingsForm />
        </div>
      </div>
    </ToastProvider>
  );
}
