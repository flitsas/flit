"use client";

import { useParams } from "next/navigation";
import { useState } from "react";
import { ToastProvider } from "@/components/admin/Toast";
import { OtHubLayout } from "@/components/admin/transit-offices/OtHubLayout";
import {
  OtFormatoContratoChip,
  OtMandatosSection,
  type OtFormatoVista,
} from "@/components/admin/transit-offices/OtMandatosSection";

export default function OtMandatosPage() {
  return (
    <ToastProvider>
      <OtMandatosPageInner />
    </ToastProvider>
  );
}

function OtMandatosPageInner() {
  const params = useParams<{ id: string }>();
  const [formato, setFormato] = useState<OtFormatoVista | null>(null);

  return (
    <OtHubLayout
      transitOfficeId={params.id}
      activeTab="mandatos"
      moduleTitle="Administración OT — Mandatos"
      surface="plano"
      titleRight={<OtFormatoContratoChip formato={formato} />}
    >
      <OtMandatosSection transitOfficeId={params.id} onFormatoChange={setFormato} />
    </OtHubLayout>
  );
}
