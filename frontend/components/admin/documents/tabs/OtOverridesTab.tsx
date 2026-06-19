"use client";

import { useCallback, useEffect, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { OrderOverrideForm } from "@/components/admin/documents/panels/OrderOverrideForm";
import { OverridesList } from "@/components/admin/documents/panels/OverridesList";
import { OtRequirementsList } from "@/components/admin/documents/panels/OtRequirementsList";
import { fetchTransitOffices } from "@/lib/api/admin-companies";
import { fetchProcedureDocumentRequirements } from "@/lib/api/admin-procedure-documents";
import {
  createDocumentOrderOverride,
  fetchDocumentOrderOverrides,
  removeDocumentOrderOverride,
} from "@/lib/api/admin-document-overrides";
import {
  fetchDocumentRequirementOverrides,
  setDocumentRequirementOverride,
} from "@/lib/api/admin-document-requirement-overrides";
import {
  persistReorderedOverrides,
  renumberOverrides,
  toDocumentOptions,
} from "@/components/admin/documents/panels/overrideDocuments";
import type {
  DocumentOrderOverride,
  DocumentRequirementSelection,
  DocumentType,
} from "@/lib/api/types-documents";
import type { TransitOffice } from "@/lib/api/types";

// Tab de configuración por Organismo de Tránsito (HU #10198). Selector de OT (catálogo
// #10192) → dos secciones: (1) overrides de ORDEN scope=OT (alta/baja + arrastre) y
// (2) OBLIGATORIEDAD por OT (3 estados: Obligatorio/Opcional/No aplica, granular solo OT).
// 4 estados UI (AC7) sobre la sección de overrides de orden.
export function OtOverridesTab({ procedureTypeId }: { procedureTypeId: string }) {
  const { show } = useToast();
  const [offices, setOffices] = useState<TransitOffice[]>([]);
  // Solo los documentos asociados al trámite admiten override: el backend rechaza
  // (422) cualquier otro. Alimentamos el selector con esos, no con todo el catálogo.
  const [associatedDocs, setAssociatedDocs] = useState<DocumentType[]>([]);
  const [transitOfficeId, setTransitOfficeId] = useState("");
  const [status, setStatus] = useState<UiStatus>("empty");
  const [overrides, setOverrides] = useState<DocumentOrderOverride[]>([]);
  const [busy, setBusy] = useState(false);
  // Obligatoriedad por OT (HU #10198): estado efectivo por documento (ausencia = «DEFAULT»).
  const [requirementSelection, setRequirementSelection] = useState<
    Record<string, DocumentRequirementSelection>
  >({});
  const [reqBusy, setReqBusy] = useState(false);

  // Catálogos base (OT + documentos asociados al trámite) — una sola vez.
  useEffect(() => {
    const controller = new AbortController();
    void (async () => {
      try {
        const [ots, requirements] = await Promise.all([
          fetchTransitOffices(undefined, controller.signal),
          fetchProcedureDocumentRequirements(procedureTypeId, controller.signal),
        ]);
        if (controller.signal.aborted) return;
        setOffices(ots);
        setAssociatedDocs(toDocumentOptions(requirements));
      } catch {
        /* el selector queda vacío; la sección de overrides gestiona su propio estado */
      }
    })();
    return () => controller.abort();
  }, [procedureTypeId]);

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

  // Carga la obligatoriedad por OT al cambiar de OT. Solo trae los documentos con override;
  // los demás quedan en «DEFAULT» (heredan del trámite).
  const loadRequirements = useCallback(
    async (signal?: AbortSignal) => {
      if (!transitOfficeId) {
        setRequirementSelection({});
        return;
      }
      try {
        const data = await fetchDocumentRequirementOverrides(procedureTypeId, transitOfficeId, signal);
        if (signal?.aborted) return;
        const map: Record<string, DocumentRequirementSelection> = {};
        for (const o of data) {
          map[o.documentTypeId] = o.estado;
        }
        setRequirementSelection(map);
      } catch {
        /* la sección muestra «Por defecto» si falla la carga */
      }
    },
    [procedureTypeId, transitOfficeId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadRequirements(controller.signal);
    return () => controller.abort();
  }, [loadRequirements]);

  // Cambia la obligatoriedad de un documento para este OT (optimista). «DEFAULT» limpia.
  const handleRequirementChange = async (
    documentTypeId: string,
    selection: DocumentRequirementSelection,
  ) => {
    const previous = requirementSelection;
    setRequirementSelection((prev) => {
      const next = { ...prev };
      if (selection === "DEFAULT") {
        delete next[documentTypeId];
      } else {
        next[documentTypeId] = selection;
      }
      return next;
    });
    setReqBusy(true);
    try {
      await setDocumentRequirementOverride({
        procedureTypeId,
        documentTypeId,
        transitOfficeId,
        estado: selection,
      });
      show("Obligatoriedad actualizada.", "success");
    } catch {
      setRequirementSelection(previous);
      show("No se pudo actualizar la obligatoriedad.", "error");
    } finally {
      setReqBusy(false);
    }
  };

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
      // Al quitar el override de ORDEN de un documento, también se limpia su
      // OBLIGATORIEDAD por OT (si tenía override): ambas personalizaciones del
      // documento para este OT se eliminan juntas y vuelve a heredar del trámite.
      // El documento sigue visible en «Obligatoriedad por OT» en «Por defecto»
      // mientras siga asociado al trámite.
      if (requirementSelection[override.documentTypeId]) {
        await setDocumentRequirementOverride({
          procedureTypeId,
          documentTypeId: override.documentTypeId,
          transitOfficeId,
          estado: "DEFAULT",
        });
        setRequirementSelection((prev) => {
          const nextSelection = { ...prev };
          delete nextSelection[override.documentTypeId];
          return nextSelection;
        });
      }
      show("Override eliminado.", "success");
    } catch {
      show("No se pudo eliminar el override.", "error");
    } finally {
      setBusy(false);
    }
  };

  // La obligatoriedad por OT se configura sobre los MISMOS documentos que tienen override
  // de ORDEN en este OT. Así, al quitar un documento del orden, también desaparece de la
  // sección de obligatoriedad (membresía acoplada, no solo «Por defecto»).
  const requirementDocs: DocumentType[] = overrides.map((o) => ({
    id: o.documentTypeId,
    codigo: o.documento.codigo,
    nombre: o.documento.nombre,
    estado: "activo",
    fechaCreacion: "",
  }));

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
          <section className="flex flex-col gap-3">
            <div>
              <h3 className="text-xs font-semibold">Orden de los documentos</h3>
              <p className="text-[11px] opacity-60">
                Personaliza el orden de un documento para este OT. Arrastra (o usa ↑/↓) para
                reordenar; prevalece sobre el orden por defecto del trámite.
              </p>
            </div>
            <OrderOverrideForm
              scope="OT"
              documents={associatedDocs}
              excludeIds={overrides.map((o) => o.documentTypeId)}
              onSubmit={handleCreate}
              disabled={busy}
            />
            <UiStateBoundary
              status={status}
              onRetry={() => void load()}
              emptyMessage="Este Organismo de Tránsito no tiene overrides de orden."
              errorMessage="No se pudieron cargar los overrides."
            >
              <OverridesList
                overrides={overrides}
                onReorder={handleReorder}
                onDelete={handleDelete}
                busy={busy}
              />
            </UiStateBoundary>
          </section>

          <section className="flex flex-col gap-3 border-t pt-4" style={{ borderColor: "#DFE5ED" }}>
            <div>
              <h3 className="text-xs font-semibold">Obligatoriedad por OT</h3>
              <p className="text-[11px] opacity-60">
                Aplica a los documentos que agregaste arriba en «Orden de los documentos».
                Define si este OT los pide como obligatorio, opcional o no aplica (se oculta
                de la matriz de este OT). «Por defecto» hereda la obligatoriedad del trámite.
                Si quitas un documento del orden, también desaparece de aquí.
              </p>
            </div>
            <OtRequirementsList
              documents={requirementDocs}
              selectionByDocId={requirementSelection}
              onChange={handleRequirementChange}
              busy={reqBusy}
            />
          </section>
        </>
      ) : (
        <p className="rounded-2xl border p-6 text-center text-xs opacity-60" style={{ borderColor: "#DFE5ED" }}>
          Selecciona un Organismo de Tránsito para definir su orden documental y la
          obligatoriedad por OT.
        </p>
      )}
    </div>
  );
}
