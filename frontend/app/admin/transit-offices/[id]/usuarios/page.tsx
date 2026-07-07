"use client";

import { useParams } from "next/navigation";
import { ToastProvider } from "@/components/admin/Toast";
import { OtHubLayout } from "@/components/admin/transit-offices/OtHubLayout";
import { OtUsersSection } from "@/components/admin/transit-offices/OtUsersSection";

export default function OtUsuariosPage() {
  return (
    <ToastProvider>
      <OtUsuariosPageInner />
    </ToastProvider>
  );
}

function OtUsuariosPageInner() {
  const params = useParams<{ id: string }>();
  const transitOfficeId = params.id;

  return (
    <OtHubLayout
      transitOfficeId={transitOfficeId}
      activeTab="usuarios"
      moduleTitle="Administración OT — Usuarios"
    >
      <OtUsersSection transitOfficeId={transitOfficeId} />
    </OtHubLayout>
  );
}
