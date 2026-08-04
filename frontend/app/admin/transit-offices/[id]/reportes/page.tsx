"use client";

import { useParams } from "next/navigation";
import { ToastProvider } from "@/components/admin/Toast";
import { OtHubLayout } from "@/components/admin/transit-offices/OtHubLayout";
import { OtUnderConstructionState } from "@/components/admin/transit-offices/OtUnderConstructionState";

/** Placeholder de Reportes OT — módulo aún no construido (404 en construcción). */
export default function OtReportesPage() {
  return (
    <ToastProvider>
      <OtReportesPageInner />
    </ToastProvider>
  );
}

function OtReportesPageInner() {
  const params = useParams<{ id: string }>();

  return (
    <OtHubLayout
      transitOfficeId={params.id}
      activeTab="reportes"
      moduleTitle="Administración OT — Reportes"
    >
      <OtUnderConstructionState
        testId="ot-reportes-under-construction"
        title="Reportes en construcción"
        description="Este módulo aún no está disponible. Pronto podrás consultar reportes del organismo de tránsito desde aquí."
      />
    </OtHubLayout>
  );
}
