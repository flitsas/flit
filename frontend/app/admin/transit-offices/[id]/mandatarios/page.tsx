"use client";

import { useParams } from "next/navigation";
import { ToastProvider } from "@/components/admin/Toast";
import { OtHubLayout } from "@/components/admin/transit-offices/OtHubLayout";
import { MandatariosSection } from "@/components/admin/transit-offices/MandatariosSection";

export default function OtMandatariosPage() {
  return (
    <ToastProvider>
      <OtMandatariosPageInner />
    </ToastProvider>
  );
}

function OtMandatariosPageInner() {
  const params = useParams<{ id: string }>();

  return (
    <OtHubLayout
      transitOfficeId={params.id}
      activeTab="mandatarios"
      moduleTitle="Administración OT — Mandatario"
    >
      <MandatariosSection transitOfficeId={params.id} />
    </OtHubLayout>
  );
}
