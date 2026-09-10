"use client";

import { useParams } from "next/navigation";
import { ToastProvider } from "@/components/admin/Toast";
import { OtHubLayout } from "@/components/admin/transit-offices/OtHubLayout";
import { PlateRangesConsole } from "@/components/admin/transit-offices/PlateRangesConsole";

export default function OtPlateRangesPage() {
  return (
    <ToastProvider>
      <OtPlateRangesPageInner />
    </ToastProvider>
  );
}

function OtPlateRangesPageInner() {
  const params = useParams<{ id: string }>();

  return (
    <OtHubLayout
      transitOfficeId={params.id}
      activeTab="plate-ranges"
      moduleTitle="Administración OT — Preasignación"
    >
      <PlateRangesConsole transitOfficeId={params.id} />
    </OtHubLayout>
  );
}
