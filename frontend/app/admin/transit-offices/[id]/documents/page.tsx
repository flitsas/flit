"use client";

import { useEffect, useState } from "react";
import { useParams } from "next/navigation";
import { ToastProvider } from "@/components/admin/Toast";
import { DocumentsSection } from "@/components/admin/transit-offices/DocumentsSection";
import { OtHubLayout } from "@/components/admin/transit-offices/OtHubLayout";
import { getToken } from "@/lib/api/client";
import { decodeJwtPayload, isSuperAdmin } from "@/lib/auth/jwt";

export default function OtDocumentsPage() {
  return (
    <ToastProvider>
      <OtDocumentsPageInner />
    </ToastProvider>
  );
}

function OtDocumentsPageInner() {
  const params = useParams<{ id: string }>();
  // HU #12883 AC4 — el título depende del rol: ot_admin ("solo ordena") ya no ve Etiquetas ni el
  // switch de prenda (HU #12861), así que el título deja de mencionar "Documentos". Mismo helper
  // de rol que DocumentsSection.tsx (getToken + decodeJwtPayload + isSuperAdmin).
  const [superAdmin, setSuperAdmin] = useState(false);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- lee el rol una sola vez al montar
    setSuperAdmin(isSuperAdmin(decodeJwtPayload(getToken())));
  }, []);

  return (
    <OtHubLayout
      transitOfficeId={params.id}
      activeTab="documents"
      moduleTitle={
        superAdmin
          ? "Administración OT — Documentos y prelación"
          : "Administración OT — Prelación documental"
      }
    >
      <DocumentsSection transitOfficeId={params.id} />
    </OtHubLayout>
  );
}
