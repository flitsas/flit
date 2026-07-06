"use client";

import { useParams, useRouter } from "next/navigation";
import { ArrowLeft } from "lucide-react";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { ToastProvider } from "@/components/admin/Toast";
import { DocumentProcedureTabs } from "@/components/admin/documents/DocumentProcedureTabs";
import { findProcedureType } from "@/lib/constants/procedure-types";

// Consola documental por trámite (HU #10198, AC2–AC5/AC7). Recibe el procedureTypeId
// por la URL y orquesta las pestañas: documentos asociados, overrides OT, overrides
// Cliente y matriz resuelta. El acceso SuperAdmin lo gobierna el middleware (AC6).
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
  const procedureType = findProcedureType(procedureTypeId);

  return (
    <main className="app-bg flex min-h-screen flex-col gap-4 px-6 py-6">
      <button
        type="button"
        onClick={() => router.push("/admin/documents/procedures")}
        className="flex w-fit items-center gap-1.5 text-xs font-semibold"
        style={{ color: "#557EFF" }}
      >
        <ArrowLeft className="h-3.5 w-3.5" /> Volver a los trámites
      </button>

      <ModuleTitle
        title={procedureType ? `Documentos · ${procedureType.name}` : "Documentos del trámite"}
        subtitle={
          procedureType
            ? `Configura los documentos, overrides y la matriz resuelta del trámite ${procedureType.code}.`
            : "Configura los documentos, overrides y la matriz resuelta del trámite."
        }
      />

      <div className="flex flex-1 flex-col rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60">
        <DocumentProcedureTabs procedureTypeId={procedureTypeId} />
      </div>
    </main>
  );
}
