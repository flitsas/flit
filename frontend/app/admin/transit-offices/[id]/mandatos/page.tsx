"use client";

import { useParams } from "next/navigation";
import { ToastProvider } from "@/components/admin/Toast";
import { OtHubLayout } from "@/components/admin/transit-offices/OtHubLayout";
import { OtMandatosSection } from "@/components/admin/transit-offices/OtMandatosSection";

export default function OtMandatosPage() {
  return (
    <ToastProvider>
      <OtMandatosPageInner />
    </ToastProvider>
  );
}

function OtMandatosPageInner() {
  const params = useParams<{ id: string }>();

  return (
    <OtHubLayout
      transitOfficeId={params.id}
      activeTab="mandatos"
      moduleTitle="Administración OT — Mandatos"
    >
      <OtMandatosSection transitOfficeId={params.id} />
    </OtHubLayout>
  );
}
