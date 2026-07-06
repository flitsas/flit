"use client";

import { ArrowLeft } from "lucide-react";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { ImprontaHistorialSection } from "@/components/admin/improntas/ImprontaHistorialSection";
import { ImprontasTabs } from "@/components/admin/improntas/ImprontasTabs";

// Vista de historial del módulo "Generación de improntas" (HU #10470 AC1/AC2/AC3).
// Lista las improntas generadas previamente, filtrable por placa y rango de fecha.
export default function AdminImprontasHistorialPage() {
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
        title="Historial de improntas"
        subtitle="Consulta las improntas generadas previamente para tu tenant, filtrables por placa y rango de fecha."
      />

      <div
        className="flex flex-1 flex-col rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60"
      >
        <ImprontasTabs activeId="historial" />
        <div className="mt-4">
          <ImprontaHistorialSection />
        </div>
      </div>
    </div>
  );
}
