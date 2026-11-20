"use client";

import { Icono } from "@flit/ui/Icono";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { FurSimulatorPanel } from "@/components/admin/plataforma/FurSimulatorPanel";
import { ToastProvider } from "@/components/admin/Toast";

/**
 * SuperAdmin — Plataforma → FUR (HU #11700, Feature #11699).
 */
export default function AdminFurPage() {
  const router = useRouter();

  return (
    <ToastProvider>
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
          title="FUR"
          subtitle="Simula cómo se construye el Formulario Único de Registro con datos sintéticos. Solo SuperAdmin."
        />

        <div className="flex flex-1 flex-col rounded-2xl border border-border bg-card/60 p-4">
          <FurSimulatorPanel />
        </div>
      </div>
    </ToastProvider>
  );
}
