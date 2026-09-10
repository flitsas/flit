"use client";

import { ArrowLeft } from "lucide-react";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { TiposTramitePanel } from "@/components/admin/plataforma/tipos-tramite/TiposTramitePanel";
import { ToastProvider } from "@/components/admin/Toast";

/**
 * SuperAdmin — Plataforma → Tipos de trámites (ADR-0050).
 *
 * Configurador del catálogo `tramites.procedure_types`: identidad del tipo, capacidades, recorrido
 * del asistente, matriz documental y la barrera que decide si el gestor puede elegirlo al crear un
 * trámite. Hasta ahora todo eso solo se podía tocar por SQL.
 */
export default function AdminTiposTramitePage() {
  const router = useRouter();

  return (
    <ToastProvider>
      <div className="flex min-h-screen flex-col gap-4 px-4 pt-6 pb-10 md:px-6">
        <button
          type="button"
          onClick={() => router.push("/")}
          className="flex w-fit items-center gap-1.5 text-xs font-semibold"
          style={{ color: "#557EFF" }}
        >
          <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
          Volver al inicio
        </button>

        <ModuleTitle
          title="Tipos de trámites"
          subtitle="Catálogo de trámites y su parametrización: qué recorrido sigue cada uno, qué exige y qué documentos pide. Solo SuperAdmin."
        />

        <div className="flex flex-1 flex-col rounded-2xl border border-[#DFE5ED] bg-white/60 p-4 dark:border-white/10 dark:bg-[#0B0F14]/60">
          <TiposTramitePanel />
        </div>
      </div>
    </ToastProvider>
  );
}
