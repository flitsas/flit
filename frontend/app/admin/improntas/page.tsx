"use client";

import { ArrowLeft } from "lucide-react";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { ImprontaFormPanel } from "@/components/admin/improntas/ImprontaFormPanel";
import { ImprontasTabs } from "@/components/admin/improntas/ImprontasTabs";

// Landing del módulo "Generación de improntas" — formulario de captura (HU #10469 AC1).
// La generación real y descarga fina de errores por tipo (validación/auth/upstream de
// Kyverum) es alcance de la HU #10471; aquí el formulario ya queda listo para conectar.
export default function AdminImprontasPage() {
  const router = useRouter();

  return (
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
        title="Generación de improntas"
        subtitle="Genera el Certificado de Improntas Digitales del vehículo (Res. 17145/2023 Mintransporte) y descárgalo en tu equipo."
      />

      <div
        className="flex flex-1 flex-col rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60"
      >
        <ImprontasTabs activeId="formulario" />
        <div className="mt-4">
          <ImprontaFormPanel />
        </div>
      </div>
    </div>
  );
}
