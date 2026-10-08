"use client";

import { ArrowLeft } from "lucide-react";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { ParametrosMotorLotePanel } from "@/components/admin/plataforma/ParametrosMotorLotePanel";

/**
 * SuperAdmin — Plataforma → Descarga masiva (HU #13420, épica #13216).
 * Parámetros del motor de lotes de consolidados: tope total, partes, carriles, tiempos, reintentos,
 * retención y encendido. Solo SuperAdmin (el backend responde 403 a cualquier otro rol y la entrada
 * del menú no se declara sin el rol).
 */
export default function AdminDescargaMasivaPage() {
  const router = useRouter();

  return (
    <div className="flex min-h-screen flex-col gap-4 px-4 pt-6 pb-10 md:px-6">
      <button
        type="button"
        onClick={() => router.push("/")}
        className="flex w-fit items-center gap-1.5 rounded-md text-xs font-semibold text-flit-brand-ink focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand"
      >
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
        Volver al inicio
      </button>

      <ModuleTitle
        title="Descarga masiva"
        subtitle="Parámetros del motor de descarga masiva de consolidados. El motor los aplica en su siguiente ciclo, sin reiniciar. Solo SuperAdmin."
      />

      <div className="flex flex-1 flex-col rounded-2xl border border-border bg-card/60 p-4">
        <ParametrosMotorLotePanel />
      </div>
    </div>
  );
}
