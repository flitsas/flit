"use client";

import { useParams, useRouter } from "next/navigation";
import { ArrowLeft } from "lucide-react";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { ToastProvider } from "@/components/admin/Toast";
import { DocumentProcedureTabs } from "@/components/admin/documents/DocumentProcedureTabs";
import { useProcedureTypes } from "@/hooks/useProcedureTypes";
import { ADMIN_BACK_LINK_CLS, ADMIN_CONTENT_SURFACE_CLS } from "@/components/admin/admin-ui-styles";

// Consola documental por trámite (HU #10198, AC2–AC5/AC7). Recibe el procedureTypeId
// por la URL y orquesta las pestañas: documentos asociados, overrides OT y matriz
// resuelta. El acceso SuperAdmin lo gobierna el middleware (AC6).
export default function DocumentProcedurePage() {
  return (
    <ToastProvider>
      <DocumentProcedureConsole />
    </ToastProvider>
  );
}

function DocumentProcedureConsole() {
  const router = useRouter();
  const params = useParams<{ procedureTypeId: string }>();
  const procedureTypeId = params.procedureTypeId;
  const { items } = useProcedureTypes();
  const procedureType = items.find((p) => p.id === procedureTypeId);

  return (
    <main className="app-bg flex min-h-screen flex-col gap-4 px-6 py-6">
      <button
        type="button"
        onClick={() => router.push("/admin/documents/procedures")}
        className={ADMIN_BACK_LINK_CLS}
      >
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" /> Volver a los trámites
      </button>

      <ModuleTitle
        title={procedureType ? `Documentos · ${procedureType.name}` : "Documentos del trámite"}
        subtitle={
          procedureType
            ? `Configura los documentos, overrides y la matriz resuelta del trámite ${procedureType.code}.`
            : "Configura los documentos, overrides y la matriz resuelta del trámite."
        }
      />

      <div className={ADMIN_CONTENT_SURFACE_CLS}>
        <DocumentProcedureTabs procedureTypeId={procedureTypeId} />
      </div>
    </main>
  );
}
