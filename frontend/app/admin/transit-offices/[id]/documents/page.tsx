"use client";

import { ArrowLeft } from "lucide-react";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { ToastProvider } from "@/components/admin/Toast";
import { DocumentsSection } from "@/components/admin/transit-offices/DocumentsSection";

export default function OtDocumentsPage() {
  return (
    <ToastProvider>
      <OtDocumentsPageInner />
    </ToastProvider>
  );
}

function OtDocumentsPageInner() {
  const router = useRouter();

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
      <ModuleTitle title="Administración OT — Documentos y etiquetas" />
      <div
        className="mt-4 rounded-2xl border bg-white p-4 md:p-6"
        style={{ borderColor: "#DFE5ED" }}
      >
        <DocumentsSection />
      </div>
    </main>
  );
}
