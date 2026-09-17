"use client";

import { useParams } from "next/navigation";
import { OtHubLayout } from "@/components/admin/transit-offices/OtHubLayout";
import { RevocationRequestsSection } from "@/components/admin/transit-offices/RevocationRequestsSection";

export default function OtRevocationRequestsPage() {
  const params = useParams<{ id: string }>();

  return (
    <OtHubLayout
      transitOfficeId={params.id}
      activeTab="revocation-requests"
      moduleTitle="Revocatorias"
      // Sin la tarjeta del hub, mismo criterio que "Trámites OT" (`client-procedures/page.tsx`): la
      // pantalla es una pila de bloques propios (filtros, tabla) sobre el fondo claro.
      surface="plano"
    >
      <RevocationRequestsSection transitOfficeId={params.id} />
    </OtHubLayout>
  );
}
