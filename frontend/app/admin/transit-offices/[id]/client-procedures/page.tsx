"use client";

import { useParams } from "next/navigation";
import { ToastProvider } from "@/components/admin/Toast";
import { ClientProceduresSection } from "@/components/admin/transit-offices/ClientProceduresSection";
import { OtHubLayout } from "@/components/admin/transit-offices/OtHubLayout";

export default function OtClientProceduresPage() {
  return (
    <ToastProvider>
      <OtClientProceduresPageInner />
    </ToastProvider>
  );
}

function OtClientProceduresPageInner() {
  const params = useParams<{ id: string }>();

  return (
    <OtHubLayout
      transitOfficeId={params.id}
      activeTab="client-procedures"
      moduleTitle="Administración OT — Trámites de clientes"
    >
      <ClientProceduresSection />
    </OtHubLayout>
  );
}
