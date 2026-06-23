"use client";

import { useParams } from "next/navigation";
import { ToastProvider } from "@/components/admin/Toast";
import { OtHubLayout } from "@/components/admin/transit-offices/OtHubLayout";
import { RulesSection } from "@/components/admin/transit-offices/RulesSection";

export default function OtRulesPage() {
  return (
    <ToastProvider>
      <OtRulesPageInner />
    </ToastProvider>
  );
}

function OtRulesPageInner() {
  const params = useParams<{ id: string }>();

  return (
    <OtHubLayout
      transitOfficeId={params.id}
      activeTab="rules"
      moduleTitle="Administración OT — Motor de reglas"
    >
      <RulesSection />
    </OtHubLayout>
  );
}
