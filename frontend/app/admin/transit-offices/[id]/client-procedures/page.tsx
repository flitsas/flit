"use client";

import { ArrowLeft } from "lucide-react";
import { useParams, useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { ToastProvider } from "@/components/admin/Toast";
import { ClientProceduresSection } from "@/components/admin/transit-offices/ClientProceduresSection";

export default function OtClientProceduresPage() {
  return (
    <ToastProvider>
      <OtClientProceduresPageInner />
    </ToastProvider>
  );
}

function OtClientProceduresPageInner() {
  const router = useRouter();
  useParams<{ id: string }>();

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
      <ModuleTitle title="Administración OT — Trámites de clientes" />
      <div className="mt-4 rounded-2xl border bg-white p-4 md:p-6" style={{ borderColor: "#DFE5ED" }}>
        <ClientProceduresSection />
      </div>
    </main>
  );
}
