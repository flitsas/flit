"use client";

import { useParams } from "next/navigation";
import { ToastProvider } from "@/components/admin/Toast";
import { OtHubLayout } from "@/components/admin/transit-offices/OtHubLayout";
import { OtReportsConsole } from "@/components/admin/transit-offices/OtReportsConsole";

/**
 * Reportes del organismo de tránsito.
 *
 * Antes esta ruta era un cartel de «módulo en construcción»: el organismo trabajaba dentro de FLIT
 * sin ningún instrumento para ver su propia operación, mientras la empresa gestora sí tenía cinco
 * tableros. Aquí se cierra esa asimetría.
 */
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
      <OtReportsConsole transitOfficeId={params.id} />
    </OtHubLayout>
  );
}
