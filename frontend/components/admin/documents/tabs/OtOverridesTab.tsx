"use client";

import { useCallback, useEffect, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { OrderOverrideForm } from "@/components/admin/documents/panels/OrderOverrideForm";
import { OverridesList } from "@/components/admin/documents/panels/OverridesList";
import { fetchTransitOffices } from "@/lib/api/admin-companies";
import { fetchDocumentTypes } from "@/lib/api/admin-document-types";
import {
  createDocumentOrderOverride,
  fetchDocumentOrderOverrides,
  removeDocumentOrderOverride,
} from "@/lib/api/admin-document-overrides";
import type { DocumentOrderOverride, DocumentType } from "@/lib/api/types-documents";
import type { TransitOffice } from "@/lib/api/types";

// Tab de overrides de orden por Organismo de Tránsito (HU #10198, AC3). Selector de
// OT (catálogo #10192) → lista de overrides scope=OT con badge "OT" + alta/baja.
// 4 estados UI (AC7) sobre la sección de overrides.
export function OtOverridesTab({ procedureTypeId }: { procedureTypeId: string }) {
  const { show } = useToast();
  const [offices, setOffices] = useState<TransitOffice[]>([]);
  const [catalog, setCatalog] = useState<DocumentType[]>([]);
  const [transitOfficeId, setTransitOfficeId] = useState("");
  const [status, setStatus] = useState<UiStatus>("empty");
  const [overrides, setOverrides] = useState<DocumentOrderOverride[]>([]);
  const [busy, setBusy] = useState(false);

  // Catálogos base (OT + documentos activos) — una sola vez.
  useEffect(() => {
    const controller = new AbortController();
    void (async () => {
      try {
        const [ots, docs] = await Promise.all([
          fetchTransitOffices(undefined, controller.signal),
          fetchDocumentTypes({ page: 1, pageSize: 100 }, controller.signal),
        ]);
        if (controller.signal.aborted) return;
        setOffices(ots);
        setCatalog(docs.data);
      } catch {
        /* el selector queda vacío; la sección de overrides gestiona su propio estado */
      }
    })();
    return () => controller.abort();
  }, []);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      if (!transitOfficeId) {
        setOverrides([]);
        setStatus("empty");
        return;
      }
      setStatus("loading");
      try {
        const data = await fetchDocumentOrderOverrides(
          { procedureTypeId, scope: "OT", transitOfficeId },
          signal,
        );
        if (signal?.aborted) return;
        setOverrides(data);
        setStatus(data.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) setStatus("error");
      }
    },
    [procedureTypeId, transitOfficeId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const handleCreate = async (documentTypeId: string, orden: number) => {
    const created = await createDocumentOrderOverride("OT", {
      procedureTypeId,
      documentTypeId,
      transitOfficeId,
      orden,
    });
    setOverrides((prev) => [...prev, created].sort((a, b) => a.orden - b.orden));
    setStatus("ready");
    show("Override OT creado.", "success");
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
      <div>
        <label htmlFor="ot-override-select" className="mb-1 block text-xs font-semibold">
          Organismo de Tránsito
        </label>
        <select
          id="ot-override-select"
          value={transitOfficeId}
          onChange={(e) => setTransitOfficeId(e.target.value)}
          className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
          style={{ borderColor: "#DFE5ED" }}
        >
          <option value="">Selecciona un Organismo de Tránsito…</option>
          {offices.map((o) => (
            <option key={o.id} value={o.id}>
              {o.name} ({o.code})
            </option>
          ))}
        </select>
      </div>

      {transitOfficeId ? (
        <>
          <OrderOverrideForm scope="OT" documents={catalog} onSubmit={handleCreate} disabled={busy} />
          <UiStateBoundary
            status={status}
            onRetry={() => void load()}
            emptyMessage="Este Organismo de Tránsito no tiene overrides de orden."
            errorMessage="No se pudieron cargar los overrides."
          >
            <OverridesList overrides={overrides} onDelete={handleDelete} busy={busy} />
          </UiStateBoundary>
        </>
      ) : (
        <p className="rounded-2xl border p-6 text-center text-xs opacity-60" style={{ borderColor: "#DFE5ED" }}>
          Selecciona un Organismo de Tránsito para ver y definir sus overrides de orden.
        </p>
      )}
    </div>
  );
}
