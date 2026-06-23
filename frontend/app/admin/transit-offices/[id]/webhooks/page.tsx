"use client";

import { useParams } from "next/navigation";
import { ToastProvider } from "@/components/admin/Toast";
import { OtHubLayout } from "@/components/admin/transit-offices/OtHubLayout";
import { WebhooksSection } from "@/components/admin/transit-offices/WebhooksSection";

export default function OtWebhooksPage() {
  return (
    <ToastProvider>
      <OtWebhooksPageInner />
    </ToastProvider>
  );
}

function OtWebhooksPageInner() {
  const params = useParams<{ id: string }>();

  return (
    <OtHubLayout
      transitOfficeId={params.id}
      activeTab="webhooks"
      moduleTitle="Administración OT — Webhooks e integración"
    >
      <WebhooksSection />
    </OtHubLayout>
  );
}
