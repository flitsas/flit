"use client";

import { useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { ArrowLeft, ArrowRight, Search } from "lucide-react";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { useProcedureTypes } from "@/hooks/useProcedureTypes";
import { SEARCH_TEXT_MAX_LENGTH, sanitizeNoAngleBrackets } from "@/lib/validation/fieldRules";

// Lista de tipos de trámite para la consola documental (HU #10198, AC2–AC5).
// Un solo camino: tarjetas. El select duplicado se retiró porque hacía lo mismo.
export default function DocumentProceduresPage() {
  const router = useRouter();
  const { items } = useProcedureTypes();
  const [q, setQ] = useState("");
  const procedureTypes = items.filter((p) => p.isActive);

  const filtered = useMemo(() => {
    const term = q.trim().toLowerCase();
    if (!term) {
      return procedureTypes;
    }
    return procedureTypes.filter(
      (p) =>
        p.name.toLowerCase().includes(term) || p.code.toLowerCase().includes(term),
    );
  }, [procedureTypes, q]);

  const go = (id: string) => {
    router.push(`/admin/documents/procedures/${id}`);
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

      <div className="flex flex-col gap-4 rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60">
        <div className="max-w-md">
          <label className="mb-1 block text-xs font-semibold" htmlFor="procedure-type-search">
            Buscar trámite
          </label>
          <div className="relative">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 opacity-50" />
            <input
              id="procedure-type-search"
              value={q}
              onChange={(e) => setQ(sanitizeNoAngleBrackets(e.target.value))}
              maxLength={SEARCH_TEXT_MAX_LENGTH}
              placeholder="Nombre o código"
              className="w-full rounded-xl border py-2 pl-9 pr-3 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
            />
          </div>
        </div>

        {filtered.length === 0 ? (
          <p className="text-xs opacity-70">Ningún tipo de trámite coincide con la búsqueda.</p>
        ) : (
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3">
            {filtered.map((p) => (
                <button
                  key={p.id}
                  type="button"
                  onClick={() => go(p.id)}
                  className="flex items-center justify-between rounded-xl border bg-white px-4 py-3 text-left transition hover:border-[#557EFF] dark:bg-[#0B0F14]"
                >
                  <span>
                    <span className="block text-xs font-semibold">{p.name}</span>
                    <span className="block font-mono text-[10px] opacity-60">{p.code}</span>
                  </span>
                  <ArrowRight className="h-4 w-4" style={{ color: "#557EFF" }} />
                </button>
              ))}
          </div>
        )}
      </div>
    </main>
  );
}
