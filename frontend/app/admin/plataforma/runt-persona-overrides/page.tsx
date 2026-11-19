"use client";

/**
 * SuperAdmin — Plataforma → Overrides de persona RUNT (HU #14642).
 *
 * Permite crear, editar y desactivar registros que substituyen la respuesta del RUNT para una
 * persona específica dentro de un tenant. Acceso exclusivo SuperAdmin: la policy del backend
 * (SuperAdminPolicy) lo garantiza; la comprobación aquí es defensa en profundidad.
 */
import { Icono } from "@flit/ui/Icono";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { ToastProvider } from "@/components/admin/Toast";
import { RuntPersonaOverridesPanel } from "@/components/admin/plataforma/runt-persona-overrides/RuntPersonaOverridesPanel";

export default function AdminRuntPersonaOverridesPage() {
  return (
    <ToastProvider>
      <PageContent />
    </ToastProvider>
  );
}

function PageContent() {
  const router = useRouter();

  return (
    <div className="flex min-h-screen flex-col gap-4 px-4 pt-6 pb-10 md:px-6">
      <button
        type="button"
        onClick={() => router.push("/")}
        className="flex w-fit items-center gap-1.5 text-xs font-semibold"
        style={{ color: "var(--color-flit-brand)" }}
      >
        <Icono nombre="izquierda" className="h-3.5 w-3.5" />
        Volver al inicio
      </button>

      <ModuleTitle
        title="Overrides de persona RUNT"
        subtitle="Registros que substituyen la respuesta del RUNT para una persona específica dentro de un tenant. Útil para manejar documentos que el RUNT no resuelve correctamente (p. ej. TI). Solo SuperAdmin."
      />

      <RuntPersonaOverridesPanel />
    </div>
  );
}
