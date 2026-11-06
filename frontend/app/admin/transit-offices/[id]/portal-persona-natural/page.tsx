"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { ToastProvider } from "@/components/admin/Toast";
import { OtHubLayout } from "@/components/admin/transit-offices/OtHubLayout";
import { OtPreciosSection } from "@/components/admin/transit-offices/portal-pn/OtPreciosSection";
import { OtPortalDomainSection } from "@/components/admin/transit-offices/portal-pn/OtPortalDomainSection";
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
        <nav aria-label="Miga de pan" className="mb-3 text-xs" style={{ color: "#162744" }}>
          <Link href="/" className="hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF]">
            Inicio
          </Link>{" "}
          &gt; <span aria-current="page" className="font-semibold">Portal Persona Natural</span>
        </nav>
        <PortalPnTabs
          transitOfficeId={params.id}
          cobro={<OtPreciosSection transitOfficeId={params.id} />}
          domain={<OtPortalDomainSection transitOfficeId={params.id} />}
        />
      </PortalPnAccess>
    </OtHubLayout>
  );
}
