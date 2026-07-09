"use client";

import { useParams } from "next/navigation";
import { ToastProvider } from "@/components/admin/Toast";
import { OtHubLayout } from "@/components/admin/transit-offices/OtHubLayout";
import { RequirementsSection } from "@/components/admin/transit-offices/RequirementsSection";

export default function OtRequirementsPage() {
  return (
    <ToastProvider>
      <OtRequirementsPageInner />
    </ToastProvider>
  );
}

function OtRequirementsPageInner() {
  const params = useParams<{ id: string }>();

  return (
    <OtHubLayout
      transitOfficeId={params.id}
      activeTab="requirements"
      moduleTitle="Administración OT — Requisitos"
    >
      <RequirementsSection transitOfficeId={params.id} />
    </OtHubLayout>
  );
}
