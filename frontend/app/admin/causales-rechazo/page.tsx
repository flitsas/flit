"use client";

import { RejectionReasonsConsole } from "@/components/admin/rejection-reasons/RejectionReasonsConsole";

/**
 * Catálogo global de causales de rechazo (SuperAdmin).
 *
 * Es global y no por organismo a propósito: si cada organismo definiera las suyas, veinte
 * organismos inventarían veinte formas de decir «improntas borrosas» y el reporte de motivos
 * dejaría de ser comparable entre organismos y entre empresas, que es justo para lo que existe.
 */
export default function AdminRejectionReasonsPage() {
  return (
    <main className="mx-auto flex w-full max-w-5xl flex-col gap-6 px-4 py-8">
      <header className="flex flex-col gap-1">
        <h1 className="text-xl font-semibold">Causales de rechazo</h1>
        <p className="text-xs text-[#6B7280] dark:text-white/50">
          Lista que ve el revisor del organismo al rechazar un trámite. Puede marcar varias en un
          mismo rechazo, y acompañarlas de una observación en texto libre.
        </p>
      </header>
      <RejectionReasonsConsole />
    </main>
  );
}
