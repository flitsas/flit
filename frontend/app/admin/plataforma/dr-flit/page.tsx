"use client";

import { Icono } from "@flit/ui/Icono";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { DrFlitVisibilidadPanel } from "@/components/admin/plataforma/DrFlitVisibilidadPanel";

/**
 * SuperAdmin — Plataforma → Asistente DR. FLIT (HU #14772, Feature #14769).
 */
export default function AdminDrFlitPage() {
  const router = useRouter();

  return (
    <div className="flex flex-col gap-4 px-4 pt-6 pb-10 md:px-6">
      <button
        type="button"
        onClick={() => router.push("/")}
        className="flex w-fit items-center gap-1.5 rounded-md text-xs font-semibold text-flit-brand-ink focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand"
      >
        <Icono nombre="izquierda" className="h-3.5 w-3.5" />
        Volver al inicio
      </button>

      <ModuleTitle
        title="Asistente DR. FLIT"
        subtitle="Activa o desactiva el botón flotante y el panel del asistente para toda la plataforma. Solo SuperAdmin."
      />

      <div className="flex flex-1 flex-col rounded-2xl border border-border bg-card/60 p-4">
        <DrFlitVisibilidadPanel />
      </div>
    </div>
  );
}
