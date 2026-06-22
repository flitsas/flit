"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { ArrowLeft, ArrowRight } from "lucide-react";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { ProcedureTypeSelect } from "@/components/admin/documents/ProcedureTypeSelect";
import { PROCEDURE_TYPES } from "@/lib/constants/procedure-types";

// Selector de tipo de trámite (HU #10198, AC2–AC5). Como no existe endpoint de
// tipos de trámite, se usa la lista estática; al elegir uno se navega a la consola
// documental por trámite (`/admin/documents/procedures/[procedureTypeId]`).
export default function DocumentProceduresPage() {
  const router = useRouter();
  const [procedureTypeId, setProcedureTypeId] = useState("");

  const go = (id: string) => {
    if (id) {
      router.push(`/admin/documents/procedures/${id}`);
    }
  };

  return (
    <main className="app-bg flex min-h-screen flex-col gap-4 px-6 py-6">
      <button
        type="button"
        onClick={() => router.push("/admin/documents")}
        className="flex w-fit items-center gap-1.5 text-xs font-semibold"
        style={{ color: "#557EFF" }}
      >
        <ArrowLeft className="h-3.5 w-3.5" /> Volver al catálogo
      </button>

      <ModuleTitle
        title="Configuración documental por trámite"
        subtitle="Selecciona un tipo de trámite para gestionar sus documentos, overrides y matriz resuelta."
      />

      <div className="flex flex-col gap-4 rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60" style={{ borderColor: "#DFE5ED" }}>
        <div className="max-w-md">
          <ProcedureTypeSelect value={procedureTypeId} onChange={setProcedureTypeId} />
          <button
            type="button"
            onClick={() => go(procedureTypeId)}
            disabled={!procedureTypeId}
            className="mt-3 inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
            style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
          >
            Configurar <ArrowRight className="h-3.5 w-3.5" />
          </button>
        </div>

        <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3">
          {PROCEDURE_TYPES.map((p) => (
            <button
              key={p.id}
              type="button"
              onClick={() => go(p.id)}
              className="flex items-center justify-between rounded-xl border bg-white px-4 py-3 text-left transition hover:border-[#557EFF] dark:bg-[#0B0F14]"
              style={{ borderColor: "#DFE5ED" }}
            >
              <span>
                <span className="block text-xs font-semibold">{p.name}</span>
                <span className="block font-mono text-[10px] opacity-60">{p.code}</span>
              </span>
              <ArrowRight className="h-4 w-4" style={{ color: "#557EFF" }} />
            </button>
          ))}
        </div>
      </div>
    </main>
  );
}
