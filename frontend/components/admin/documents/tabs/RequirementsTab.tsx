"use client";

import { useCallback, useEffect, useState } from "react";
import { Plus } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { DocumentAssociationList } from "@/components/admin/documents/panels/DocumentAssociationList";
import { DocumentTypeSelect } from "@/components/admin/documents/DocumentTypeSelect";
import { fetchDocumentTypes } from "@/lib/api/admin-document-types";
import {
  createProcedureDocumentRequirement,
  fetchProcedureDocumentRequirements,
  removeProcedureDocumentRequirement,
  updateProcedureDocumentRequirement,
} from "@/lib/api/admin-procedure-documents";
import { ApiError } from "@/lib/api/types";
import type { DocumentType, ProcedureDocumentRequirement } from "@/lib/api/types-documents";

const ORDER_STEP = 10;

// Tab de asociaciones documento ↔ trámite (HU #10198, AC2). Agregar (documentos
// activos), reordenar (drag-and-drop o ↑/↓ → PUT ordenDefault), togglear
// obligatoriedad y remover (DELETE, 409 si está en uso). 4 estados UI (AC7).
export function RequirementsTab({ procedureTypeId }: { procedureTypeId: string }) {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [items, setItems] = useState<ProcedureDocumentRequirement[]>([]);
  const [catalog, setCatalog] = useState<DocumentType[]>([]);
  const [newDocId, setNewDocId] = useState("");
  const [busy, setBusy] = useState(false);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const [requirements, catalogPage] = await Promise.all([
          fetchProcedureDocumentRequirements(procedureTypeId, signal),
          fetchDocumentTypes({ page: 1, pageSize: 100 }, signal),
        ]);
        if (signal?.aborted) {
          return;
        }
        setItems(requirements);
        setCatalog(catalogPage.data);
        setStatus(requirements.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) {
          setStatus("error");
        }
      }
    },
    [procedureTypeId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // Carga inicial al montar: el skeleton (setStatus loading) es intencional.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const handleAdd = async () => {
    if (!newDocId) {
      return;
    }
    setBusy(true);
    try {
      const created = await createProcedureDocumentRequirement({
        procedureTypeId,
        documentTypeId: newDocId,
        ordenDefault: (items.length + 1) * ORDER_STEP,
        obligatorio: false,
      });
      setItems((prev) => [...prev, created]);
      setNewDocId("");
      setStatus("ready");
      show(`Documento «${created.documento.nombre}» asociado.`, "success");
    } catch {
      show("No se pudo asociar el documento.", "error");
    } finally {
      setBusy(false);
    }
  };

  // Persiste el nuevo orden: PUT de cada ítem cuyo ordenDefault cambió (AC2).
  const handleReorder = async (ordered: ProcedureDocumentRequirement[]) => {
    const previous = items;
    const renumbered = ordered.map((item, index) => ({ ...item, ordenDefault: (index + 1) * ORDER_STEP }));
    setItems(renumbered); // optimista
    setBusy(true);
    try {
      const changed = renumbered.filter((item, index) => item.ordenDefault !== previous[index]?.ordenDefault || item.id !== previous[index]?.id);
      await Promise.all(
        changed.map((item) =>
          updateProcedureDocumentRequirement(item.id, {
            ordenDefault: item.ordenDefault,
            obligatorio: item.obligatorio,
          }),
        ),
      );
      show("Orden actualizado.", "success");
    } catch {
      setItems(previous); // revierte
      show("No se pudo actualizar el orden.", "error");
    } finally {
      setBusy(false);
    }
  };

  const handleToggleObligatorio = async (item: ProcedureDocumentRequirement) => {
    const next = !item.obligatorio;
    setItems((prev) => prev.map((i) => (i.id === item.id ? { ...i, obligatorio: next } : i)));
    setBusy(true);
    try {
      await updateProcedureDocumentRequirement(item.id, { ordenDefault: item.ordenDefault, obligatorio: next });
    } catch {
      setItems((prev) => prev.map((i) => (i.id === item.id ? { ...i, obligatorio: item.obligatorio } : i)));
      show("No se pudo actualizar la obligatoriedad.", "error");
    } finally {
      setBusy(false);
    }
  };

  const handleRemove = async (item: ProcedureDocumentRequirement) => {
    setBusy(true);
    try {
      await removeProcedureDocumentRequirement(item.id);
      const next = items.filter((i) => i.id !== item.id);
      setItems(next);
      setStatus(next.length === 0 ? "empty" : "ready");
      show(`Documento «${item.documento.nombre}» removido.`, "success");
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) {
        show("El documento está en uso en trámites activos y no puede removerse.", "error");
      } else {
        show("No se pudo remover el documento.", "error");
      }
    } finally {
      setBusy(false);
    }
  };

  const associatedIds = items.map((i) => i.documentTypeId);

  const adder = (
    <div className="flex flex-col gap-3 rounded-2xl border p-4 sm:flex-row sm:items-end" style={{ borderColor: "#DFE5ED" }}>
      <div className="flex-1">
        <DocumentTypeSelect
          id="req-add-doc"
          value={newDocId}
          onChange={setNewDocId}
          options={catalog}
          excludeIds={associatedIds}
          label="Agregar documento al trámite"
        />
      </div>
      <button
        type="button"
        onClick={handleAdd}
        disabled={busy || !newDocId}
        className="inline-flex items-center justify-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
        style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
      >
        <Plus className="h-3.5 w-3.5" /> Agregar
      </button>
    </div>
  );

  return (
    <div className="flex flex-col gap-4">
      {adder}
      <UiStateBoundary
        status={status}
        onRetry={() => void load()}
        emptyMessage="Este trámite no tiene documentos asociados."
        errorMessage="No se pudieron cargar los documentos del trámite."
      >
        <DocumentAssociationList
          items={items}
          onReorder={handleReorder}
          onToggleObligatorio={handleToggleObligatorio}
          onRemove={handleRemove}
          busy={busy}
        />
      </UiStateBoundary>
    </div>
  );
}
