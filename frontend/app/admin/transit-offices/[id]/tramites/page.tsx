"use client";

import { useParams } from "next/navigation";
import { ToastProvider } from "@/components/admin/Toast";
import { OtHubLayout } from "@/components/admin/transit-offices/OtHubLayout";
import { TramitesSuperSection } from "@/components/admin/transit-offices/TramitesSuperSection";

export default function OtTramitesPage() {
  return (
    <ToastProvider>
      <OtTramitesPageInner />
    </ToastProvider>
  );
}

function OtTramitesPageInner() {
  const params = useParams<{ id: string }>();
  const transitOfficeId = params.id;

  return (
    <OtHubLayout
      transitOfficeId={transitOfficeId}
      activeTab="tramites"
      moduleTitle="Administración OT — Trámites"
    >
      <TramitesSuperSection transitOfficeId={transitOfficeId} />
    </OtHubLayout>
  );
}
