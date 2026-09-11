"use client";

import { ArrowLeft } from "lucide-react";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { JobsCatalog } from "@/components/admin/jobs/JobsCatalog";
import { JobsComoFunciona } from "@/components/admin/jobs/JobsComoFunciona";

export default function AdminJobsPage() {
  const router = useRouter();

  return (
    <div className="flex min-h-screen flex-col gap-4 px-4 pt-6 pb-24 md:px-6">
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
        title="Procesos periódicos"
        subtitle="Consulta el estado de cada proceso. La cadencia se configura una sola vez por módulo."
        action={<JobsComoFunciona />}
      />

      <JobsCatalog />
    </div>
  );
}
