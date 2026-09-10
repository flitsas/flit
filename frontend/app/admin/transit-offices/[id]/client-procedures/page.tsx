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
      moduleTitle="Trámites OT"
      // Sin la tarjeta del hub: en el diseño la pantalla es una PILA de bloques sobre el fondo
      // claro —cabecera, contadores, filtros, tabla—, cada uno con su propia superficie. Metidos
      // dentro de un contenedor blanco, las filas blancas de la tabla dejaban de leerse como
      // tarjetas porque se apoyaban sobre otro blanco.
      surface="plano"
    >
      <ClientProceduresSection transitOfficeId={params.id} />
    </OtHubLayout>
  );
}
