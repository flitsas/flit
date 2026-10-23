"use client";

import { ArrowLeft } from "lucide-react";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { ImprontaHistorialSection } from "@/components/admin/improntas/ImprontaHistorialSection";
import { ImprontasTabs } from "@/components/admin/improntas/ImprontasTabs";
import { ADMIN_BACK_LINK_CLS, ADMIN_CONTENT_SURFACE_CLS } from "@/components/admin/admin-ui-styles";

// Vista de historial del módulo "Generación de improntas" (HU #10470 AC1/AC2/AC3).
// Lista las improntas generadas previamente, filtrable por placa y rango de fecha.
export default function AdminImprontasHistorialPage() {
  const router = useRouter();

  return (
    <div className="flex min-h-screen flex-col gap-4 px-6 pt-6 pb-10">
      <button type="button" onClick={() => router.push("/")} className={ADMIN_BACK_LINK_CLS}>
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
        Volver al inicio
      </button>

      <ModuleTitle
        title="Historial de improntas"
        subtitle="Consulta las improntas generadas previamente para tu tenant, filtrables por placa y rango de fecha."
      />

      <div className={ADMIN_CONTENT_SURFACE_CLS}>
        <ImprontasTabs activeId="historial" />
        <div className="mt-4">
          <ImprontaHistorialSection />
        </div>
      </div>
    </div>
  );
}
