"use client";

import { useParams } from "next/navigation";
import { ToastProvider } from "@/components/admin/Toast";
import { OtHubLayout } from "@/components/admin/transit-offices/OtHubLayout";
import { OtPreciosSection } from "@/components/admin/transit-offices/portal-pn/OtPreciosSection";
import { PortalPnAccess } from "@/components/admin/transit-offices/portal-pn/PortalPnAccess";
import { PortalPnTabs } from "@/components/admin/transit-offices/portal-pn/PortalPnTabs";

/**
 * Portal Persona Natural del organismo (Épica #14297, Feature #14374): subpestañas Cobro y Diseño.
 * SuperAdmin y administrador del organismo, con el mismo componente anclado al organismo de la ruta.
 */
export default function OtPortalPersonaNaturalPage() {
  return (
    <ToastProvider>
      <OtPortalPersonaNaturalPageInner />
    </ToastProvider>
  );
}

function OtPortalPersonaNaturalPageInner() {
  const params = useParams<{ id: string }>();

  return (
    <OtHubLayout
      transitOfficeId={params.id}
      activeTab="portal-persona-natural"
      moduleTitle="Administración OT — Portal Persona Natural"
      surface="panel"
    >
      <PortalPnAccess>
        <PortalPnTabs transitOfficeId={params.id} cobro={<OtPreciosSection transitOfficeId={params.id} />} />
      </PortalPnAccess>
    </OtHubLayout>
  );
}
