"use client";

import { useParams } from "next/navigation";
import { ToastProvider } from "@/components/admin/Toast";
import { OtHubLayout } from "@/components/admin/transit-offices/OtHubLayout";
import { OtImprintValidationSection } from "@/components/admin/transit-offices/OtImprintValidationSection";

export default function OtImprintValidationPage() {
  return (
    <ToastProvider>
      <OtImprintValidationPageInner />
    </ToastProvider>
  );
}

function OtImprintValidationPageInner() {
  const params = useParams<{ id: string }>();

  return (
    <OtHubLayout
      transitOfficeId={params.id}
      activeTab="imprint-validation"
      moduleTitle="Administración OT — Validar impronta"
      surface="plano"
    >
      <OtImprintValidationSection transitOfficeId={params.id} />
    </OtHubLayout>
  );
}
