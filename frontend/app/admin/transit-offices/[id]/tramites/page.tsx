"use client";

import { ArrowLeft } from "lucide-react";
import { useParams, useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { ToastProvider } from "@/components/admin/Toast";
import { TramitesSuperSection } from "@/components/admin/transit-offices/TramitesSuperSection";

// Consola OT — súper-sección Trámites unificada Dashboard/QX (HU #10218).
export default function OtTramitesPage() {
  return (
    <ToastProvider>
      <OtTramitesPageInner />
    </ToastProvider>
  );
}

function OtTramitesPageInner() {
  const router = useRouter();
  const params = useParams<{ id: string }>();
  const transitOfficeId = params.id;

  return (
    <main className="app-bg min-h-screen px-4 py-6 md:px-8">
      <button
        type="button"
        onClick={() => router.push("/")}
        className="mb-4 flex items-center gap-2 text-xs font-semibold"
        style={{ color: "#557EFF" }}
      >
        <ArrowLeft className="h-4 w-4" aria-hidden="true" />
        Volver
      </button>

      <ModuleTitle title="Administración OT — Trámites" />

      <div className="mt-4 rounded-2xl border bg-white p-4 md:p-6" style={{ borderColor: "#DFE5ED" }}>
        <TramitesSuperSection transitOfficeId={transitOfficeId} />
      </div>
    </main>
  );
}
