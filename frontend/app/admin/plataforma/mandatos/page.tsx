"use client";

import { Icono } from "@flit/ui/Icono";
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
          style={{ color: "var(--color-flit-brand)" }}
        >
          <Icono nombre="izquierda" className="h-3.5 w-3.5" />
          Volver al inicio
        </button>

        <ModuleTitle
          title="Mandatos"
          subtitle="Plantillas de Contrato Privado de Mandato por organismo. Solo SuperAdmin asocia la redacción; los mandatarios se asignan aparte."
        />

        <div className="flex flex-1 flex-col rounded-2xl border border-border bg-card/60 p-4">
          <MandatosCatalogPanel />
        </div>
      </div>
    </ToastProvider>
  );
}
