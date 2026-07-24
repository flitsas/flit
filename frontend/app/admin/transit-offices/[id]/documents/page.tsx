"use client";

import { useParams } from "next/navigation";
import { ToastProvider } from "@/components/admin/Toast";
import { DocumentsSection } from "@/components/admin/transit-offices/DocumentsSection";
import { OtHubLayout } from "@/components/admin/transit-offices/OtHubLayout";

export default function OtDocumentsPage() {
  return (
    <ToastProvider>
      <OtDocumentsPageInner />
    </ToastProvider>
  );
}

function OtDocumentsPageInner() {
  const params = useParams<{ id: string }>();

  return (
    <OtHubLayout
      transitOfficeId={params.id}
      activeTab="documents"
      moduleTitle="Administración OT — Documentos y prelación"
    >
      <DocumentsSection transitOfficeId={params.id} />
    </OtHubLayout>
  );
}
