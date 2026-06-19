"use client";

import { useCallback, useEffect, useState } from "react";
import { Info } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { OrderOverrideForm } from "@/components/admin/documents/panels/OrderOverrideForm";
import { OverridesList } from "@/components/admin/documents/panels/OverridesList";
import { fetchCompaniesIndex } from "@/lib/api/admin-companies";
import { fetchProcedureDocumentRequirements } from "@/lib/api/admin-procedure-documents";
import {
  createDocumentOrderOverride,
  fetchDocumentOrderOverrides,
  removeDocumentOrderOverride,
} from "@/lib/api/admin-document-overrides";
import {
  persistReorderedOverrides,
  renumberOverrides,
  toDocumentOptions,
} from "@/components/admin/documents/panels/overrideDocuments";
import type { DocumentOrderOverride, DocumentType } from "@/lib/api/types-documents";
import type { CompanyListItem } from "@/lib/api/types";

// Tab de overrides de orden por Cliente (HU #10198, AC4). Selector de Cliente
// (índice de compañías #10189) → lista de overrides scope=CLIENTE con badge
// "CLIENTE" + nota de precedencia "CLIENTE > OT > Default". 4 estados UI (AC7).
export function ClienteOverridesTab({ procedureTypeId }: { procedureTypeId: string }) {
  const { show } = useToast();
  const [companies, setCompanies] = useState<CompanyListItem[]>([]);
  // Solo los documentos asociados al trámite admiten override (el backend rechaza el
  // resto con 422); alimentamos el selector con esos, no con todo el catálogo.
  const [associatedDocs, setAssociatedDocs] = useState<DocumentType[]>([]);
  const [clienteId, setClienteId] = useState("");
  const [status, setStatus] = useState<UiStatus>("empty");
  const [overrides, setOverrides] = useState<DocumentOrderOverride[]>([]);
  const [busy, setBusy] = useState(false);

  // Catálogos base (clientes + documentos asociados al trámite) — una sola vez.
  useEffect(() => {
    const controller = new AbortController();
    void (async () => {
      try {
        const [page, requirements] = await Promise.all([
          fetchCompaniesIndex({ page: 1, pageSize: 100 }, controller.signal),
          fetchProcedureDocumentRequirements(procedureTypeId, controller.signal),
        ]);
        if (controller.signal.aborted) return;
        setCompanies(page.data);
        setAssociatedDocs(toDocumentOptions(requirements));
      } catch {
        /* el selector queda vacío; la sección de overrides gestiona su propio estado */
      }
    })();
    return () => controller.abort();
  }, [procedureTypeId]);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      if (!clienteId) {
        setOverrides([]);
        setStatus("empty");
        return;
      }
      setStatus("loading");
      try {
        const data = await fetchDocumentOrderOverrides(
          { procedureTypeId, scope: "CLIENTE", clienteId },
          signal,
        );
        if (signal?.aborted) return;
        setOverrides(data);
        setStatus(data.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) setStatus("error");
      }
    },
    [procedureTypeId, clienteId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const handleCreate = async (documentTypeId: string, orden: number) => {
    const created = await createDocumentOrderOverride("CLIENTE", {
      procedureTypeId,
      documentTypeId,
      clienteId,
      orden,
    });
    setOverrides((prev) => [...prev, created].sort((a, b) => a.orden - b.orden));
    setStatus("ready");
    show("Override de cliente creado.", "success");
  };

  // Reordenamiento por arrastre: renumera, aplica optimista y persiste los cambiados.
  const handleReorder = async (ordered: DocumentOrderOverride[]) => {
    const previous = overrides;
    const previousOrderById = new Map(previous.map((o) => [o.id, o.orden]));
    const renumbered = renumberOverrides(ordered);
    setOverrides(renumbered);
    setBusy(true);
    try {
      await persistReorderedOverrides(renumbered, previousOrderById);
      show("Orden actualizado.", "success");
    } catch {
      setOverrides(previous);
      show("No se pudo actualizar el orden.", "error");
    } finally {
      setBusy(false);
    }
  };

  const handleDelete = async (override: DocumentOrderOverride) => {
    setBusy(true);
    try {
      await removeDocumentOrderOverride(override.id);
      const next = overrides.filter((o) => o.id !== override.id);
      setOverrides(next);
      setStatus(next.length === 0 ? "empty" : "ready");
      show("Override eliminado.", "success");
    } catch {
      show("No se pudo eliminar el override.", "error");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex flex-col gap-4">
      <p
        className="flex items-start gap-1.5 rounded-xl border px-3 py-2 text-[10px] font-medium"
        style={{ borderColor: "#557EFF", background: "rgba(85,126,255,0.08)", color: "#3551b5" }}
        role="note"
      >
        <Info className="mt-px h-3.5 w-3.5 shrink-0" />
        Precedencia: CLIENTE &gt; OT &gt; Default. El orden definido para un cliente prevalece sobre el de OT y el default.
      </p>

      <div>
        <label htmlFor="cliente-override-select" className="mb-1 block text-xs font-semibold">
          Cliente
        </label>
        <select
          id="cliente-override-select"
          value={clienteId}
          onChange={(e) => setClienteId(e.target.value)}
          className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
          style={{ borderColor: "#DFE5ED" }}
        >
          <option value="">Selecciona un Cliente…</option>
          {companies.map((c) => (
            <option key={c.id} value={c.id}>
              {c.razonSocial} ({c.nit})
            </option>
          ))}
        </select>
      </div>

      {clienteId ? (
        <>
          <OrderOverrideForm
            scope="CLIENTE"
            documents={associatedDocs}
            excludeIds={overrides.map((o) => o.documentTypeId)}
            onSubmit={handleCreate}
            disabled={busy}
          />
          <UiStateBoundary
            status={status}
            onRetry={() => void load()}
            emptyMessage="Este cliente no tiene overrides de orden."
            errorMessage="No se pudieron cargar los overrides."
          >
            <OverridesList
              overrides={overrides}
              onReorder={handleReorder}
              onDelete={handleDelete}
              busy={busy}
            />
          </UiStateBoundary>
        </>
      ) : (
        <p className="rounded-2xl border p-6 text-center text-xs opacity-60" style={{ borderColor: "#DFE5ED" }}>
          Selecciona un Cliente para ver y definir sus overrides de orden.
        </p>
      )}
    </div>
  );
}
