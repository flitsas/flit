"use client";

// HU #10710 — consola de configuración operativa GLOBAL de Quipux (admin.quipux_settings).
// Ruta bajo /admin ⇒ el middleware ya la restringe a SuperAdmin (guard.ts: solo SuperAdmin
// entra a /admin/* fuera de /admin/transit-offices). No es un hub por-OT: es plataforma.
import { ArrowLeft } from "lucide-react";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { ToastProvider } from "@/components/admin/Toast";
import { JobsComoFunciona } from "@/components/admin/jobs/JobsComoFunciona";
import { QuipuxSettingsForm } from "@/components/admin/quipux/QuipuxSettingsForm";

export default function AdminQuipuxPage() {
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
          title="Integración Quipux"
          subtitle="Interruptor, direcciones, claves y cadencia con que FLIT radica en las secretarías."
          action={<JobsComoFunciona />}
        />

        <QuipuxSettingsForm />
      </div>
    </ToastProvider>
  );
}
