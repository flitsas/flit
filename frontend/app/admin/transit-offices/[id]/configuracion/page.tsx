"use client";

import { useParams } from "next/navigation";
import { ToastProvider } from "@/components/admin/Toast";
import { OtHubLayout } from "@/components/admin/transit-offices/OtHubLayout";
import { OtConfiguracionSection } from "@/components/admin/transit-offices/OtConfiguracionSection";

export default function OtConfiguracionPage() {
  return (
    <ToastProvider>
      <OtConfiguracionPageInner />
    </ToastProvider>
  );
}

function OtConfiguracionPageInner() {
  const params = useParams<{ id: string }>();
  const transitOfficeId = params.id;

  return (
    <OtHubLayout
      transitOfficeId={transitOfficeId}
      activeTab="configuracion"
      moduleTitle="Administración OT — Configuración"
    >
      <OtConfiguracionSection transitOfficeId={transitOfficeId} />
    </OtHubLayout>
  );
}
