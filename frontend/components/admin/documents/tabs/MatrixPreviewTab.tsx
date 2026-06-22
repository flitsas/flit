"use client";

import { useEffect, useState } from "react";
import { Eye } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { ResolvedMatrixTable } from "@/components/admin/documents/panels/ResolvedMatrixTable";
import { fetchTransitOffices, fetchCompaniesIndex } from "@/lib/api/admin-companies";
import { fetchResolvedDocumentMatrix } from "@/lib/api/admin-document-overrides";
import type { ResolvedDocumentMatrixRow } from "@/lib/api/types-documents";
import type { CompanyListItem, TransitOffice } from "@/lib/api/types";

// Tab de simulación de la matriz documental resuelta (HU #10198, AC5 / RF18).
// Selectores OT + Cliente (opcionales) sobre el procedureType de la URL; al pulsar
// "Ver matriz resuelta" se resuelve la precedencia Cliente > OT > Default y se
// muestra el orden final con la columna nivelAplicado. 4 estados UI (AC7).
export function MatrixPreviewTab({ procedureTypeId }: { procedureTypeId: string }) {
  const [offices, setOffices] = useState<TransitOffice[]>([]);
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  const [transitOfficeId, setTransitOfficeId] = useState("");
  const [clienteId, setClienteId] = useState("");
  const [status, setStatus] = useState<UiStatus>("empty");
  const [rows, setRows] = useState<ResolvedDocumentMatrixRow[]>([]);
  const [hasQueried, setHasQueried] = useState(false);

  // Catálogos de los selectores — una sola vez.
  useEffect(() => {
    const controller = new AbortController();
    void (async () => {
      try {
        const [ots, page] = await Promise.all([
          fetchTransitOffices(undefined, controller.signal),
          fetchCompaniesIndex({ page: 1, pageSize: 100 }, controller.signal),
        ]);
        if (controller.signal.aborted) return;
        setOffices(ots);
        setCompanies(page.data);
      } catch {
        /* los selectores quedan vacíos; el usuario aún puede resolver solo el default */
      }
    })();
    return () => controller.abort();
  }, []);

  const resolve = async () => {
    setHasQueried(true);
    setStatus("loading");
    try {
      const data = await fetchResolvedDocumentMatrix({
        procedureTypeId,
        transitOfficeId: transitOfficeId || undefined,
        clienteId: clienteId || undefined,
      });
      setRows(data);
      setStatus(data.length === 0 ? "empty" : "ready");
    } catch {
      setStatus("error");
    }
  };

  return (
    <div className="flex flex-col gap-4">
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        <div>
          <label htmlFor="matrix-ot" className="mb-1 block text-xs font-semibold">
            Organismo de Tránsito (opcional)
          </label>
          <select
            id="matrix-ot"
            value={transitOfficeId}
            onChange={(e) => setTransitOfficeId(e.target.value)}
            className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
            style={{ borderColor: "#DFE5ED" }}
          >
            <option value="">Sin OT (nivel Default/Cliente)</option>
            {offices.map((o) => (
              <option key={o.id} value={o.id}>
                {o.name} ({o.code})
              </option>
            ))}
          </select>
        </div>

        <div>
          <label htmlFor="matrix-cliente" className="mb-1 block text-xs font-semibold">
            Cliente (opcional)
          </label>
          <select
            id="matrix-cliente"
            value={clienteId}
            onChange={(e) => setClienteId(e.target.value)}
            className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
            style={{ borderColor: "#DFE5ED" }}
          >
            <option value="">Sin Cliente (nivel Default/OT)</option>
            {companies.map((c) => (
              <option key={c.id} value={c.id}>
                {c.razonSocial} ({c.nit})
              </option>
            ))}
          </select>
        </div>

        <div className="flex items-end">
          <button
            type="button"
            onClick={resolve}
            className="inline-flex w-full items-center justify-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white"
            style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
          >
            <Eye className="h-3.5 w-3.5" /> Ver matriz resuelta
          </button>
        </div>
      </div>

      {hasQueried ? (
        <UiStateBoundary
          status={status}
          onRetry={() => void resolve()}
          emptyMessage="El trámite no tiene documentos para esta combinación."
          errorMessage="No se pudo resolver la matriz documental."
        >
          <ResolvedMatrixTable rows={rows} />
        </UiStateBoundary>
      ) : (
        <p className="rounded-2xl border p-6 text-center text-xs opacity-60" style={{ borderColor: "#DFE5ED" }}>
          Elige una combinación y pulsa «Ver matriz resuelta» para simular el orden final.
        </p>
      )}
    </div>
  );
}
