"use client";

import { useCallback, useEffect, useState } from "react";
import { Trash2 } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { tramitesClient } from "@/lib/api/tramites-client";
import {
  createOtDocumentTag,
  deleteOtDocumentTag,
  fetchOtDocumentPrecedence,
  fetchOtDocumentTags,
  updateOtDocumentPrecedence,
} from "@/lib/api/admin-ot";
import type { OtDocumentPrecedenceItem, OtDocumentTag } from "@/lib/api/types-ot";
import { DocumentPrecedenceList } from "./DocumentPrecedenceList";
import { OtTabBar } from "./OtTabBar";
import { OT_INPUT_CLS } from "./ot-form-styles";
import { TagFormPanel } from "./TagFormPanel";

type Tab = "precedence" | "tags";

interface ProcedureTypeOption {
  id: string;
  name: string;
}

/** Prelación documental DnD y CRUD etiquetas OT (HU #10224). */
export function DocumentsSection() {
  const { show } = useToast();
  const [tab, setTab] = useState<Tab>("precedence");
  const [procedureTypes, setProcedureTypes] = useState<ProcedureTypeOption[]>([]);
  const [procedureTypeId, setProcedureTypeId] = useState("");
  const [precStatus, setPrecStatus] = useState<UiStatus>("loading");
  const [precedence, setPrecedence] = useState<OtDocumentPrecedenceItem[]>([]);
  const [tagStatus, setTagStatus] = useState<UiStatus>("loading");
  const [tags, setTags] = useState<OtDocumentTag[]>([]);
  const [tagFormOpen, setTagFormOpen] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<OtDocumentTag | null>(null);
  const [deleting, setDeleting] = useState(false);

  useEffect(() => {
    tramitesClient
      .listPublishedProcedureTypes()
      .then((list) => {
        const opts = list.map((p) => ({ id: p.id, name: p.name }));
        setProcedureTypes(opts);
        if (opts[0]) setProcedureTypeId(opts[0].id);
      })
      .catch(() => setPrecStatus("error"));
  }, []);

  const loadPrecedence = useCallback(
    async (signal?: AbortSignal) => {
      if (!procedureTypeId) {
        setPrecStatus("empty");
        return;
      }
      setPrecStatus("loading");
      try {
        const result = await fetchOtDocumentPrecedence(procedureTypeId, signal);
        if (signal?.aborted) return;
        setPrecedence(result.data);
        setPrecStatus(result.data.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) setPrecStatus("error");
      }
    },
    [procedureTypeId],
  );

  const loadTags = useCallback(async (signal?: AbortSignal) => {
    setTagStatus("loading");
    try {
      const result = await fetchOtDocumentTags(signal);
      if (signal?.aborted) return;
      setTags(result.data);
      setTagStatus(result.data.length === 0 ? "empty" : "ready");
    } catch {
      if (!signal?.aborted) setTagStatus("error");
    }
  }, []);

  useEffect(() => {
    if (tab !== "precedence") return;
    const c = new AbortController();
    void loadPrecedence(c.signal);
    return () => c.abort();
  }, [tab, loadPrecedence]);

  useEffect(() => {
    if (tab !== "tags") return;
    const c = new AbortController();
    void loadTags(c.signal);
    return () => c.abort();
  }, [tab, loadTags]);

  const handleReorder = async (items: OtDocumentPrecedenceItem[]) => {
    const result = await updateOtDocumentPrecedence({
      procedure_type_id: procedureTypeId,
      items: items.map((i) => ({
        document_type_id: i.document_type_id,
        sort_order: i.sort_order,
      })),
    });
    setPrecedence(result.data);
    show("Orden actualizado.", "success");
  };

  const confirmDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await deleteOtDocumentTag(deleteTarget.id);
      setTags((prev) => prev.filter((t) => t.id !== deleteTarget.id));
      setTagStatus((s) => (tags.length <= 1 ? "empty" : s));
      show("Etiqueta eliminada.", "success");
      setDeleteTarget(null);
    } catch {
      show("No se pudo eliminar la etiqueta.", "error");
    } finally {
      setDeleting(false);
    }
  };

  return (
    <div className="space-y-4">
      <OtTabBar
        ariaLabel="Secciones documentales"
        tabs={[
          { id: "precedence", label: "Prelación" },
          { id: "tags", label: "Etiquetas" },
        ]}
        activeId={tab}
        onChange={(id) => setTab(id as Tab)}
      />

      {tab === "precedence" && (
        <div role="tabpanel" className="space-y-3 pt-2">
          <label className="block max-w-md text-xs font-semibold" style={{ color: "#162744" }}>
            Tipo de trámite
            <select
              className={`mt-1 ${OT_INPUT_CLS}`}
              value={procedureTypeId}
              onChange={(e) => setProcedureTypeId(e.target.value)}
              aria-label="Tipo de trámite"
            >
              {procedureTypes.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </select>
          </label>

          <UiStateBoundary
            status={precStatus}
            emptyMessage="No hay documentos configurados para este trámite."
            errorMessage="Error al cargar la prelación."
            onRetry={() => void loadPrecedence()}
            skeletonRows={4}
          >
            <DocumentPrecedenceList items={precedence} onReorder={handleReorder} />
          </UiStateBoundary>
        </div>
      )}

      {tab === "tags" && (
        <div role="tabpanel" className="space-y-3 pt-2">
          <div className="flex justify-end">
            <button
              type="button"
              className="rounded-xl px-4 py-2 text-xs font-semibold text-white"
              style={{ background: "#557EFF" }}
              onClick={() => setTagFormOpen(true)}
            >
              Nueva etiqueta
            </button>
          </div>

          <UiStateBoundary
            status={tagStatus}
            emptyMessage="No hay etiquetas configuradas."
            emptyCta={
              <button
                type="button"
                className="rounded-xl px-4 py-2 text-xs font-semibold text-white"
                style={{ background: "#557EFF" }}
                onClick={() => setTagFormOpen(true)}
              >
                Crear primera etiqueta
              </button>
            }
            errorMessage="Error al cargar etiquetas."
            onRetry={() => void loadTags()}
            skeletonRows={3}
          >
            <ul className="flex flex-wrap gap-2" aria-label="Etiquetas documentales">
              {tags.map((tag) => (
                <li key={tag.id}>
                  <span
                    className="inline-flex items-center gap-2 rounded-full px-3 py-1.5 text-[11px] font-semibold text-white"
                    style={{ background: tag.color }}
                  >
                    {tag.name}
                    <button
                      type="button"
                      aria-label={`Eliminar etiqueta ${tag.name}`}
                      className="rounded-full bg-black/20 p-0.5 hover:bg-black/30"
                      onClick={() => setDeleteTarget(tag)}
                    >
                      <Trash2 className="h-3 w-3" />
                    </button>
                  </span>
                </li>
              ))}
            </ul>
          </UiStateBoundary>
        </div>
      )}

      <TagFormPanel
        open={tagFormOpen}
        onClose={() => setTagFormOpen(false)}
        onCreate={createOtDocumentTag}
        onSaved={(tag) => {
          setTags((prev) => [...prev, tag]);
          setTagStatus("ready");
          setTagFormOpen(false);
          show("Etiqueta creada.", "success");
        }}
      />

      {deleteTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <button
            type="button"
            className="absolute inset-0 bg-slate-900/40"
            aria-label="Cerrar"
            onClick={() => !deleting && setDeleteTarget(null)}
          />
          <div
            role="alertdialog"
            aria-labelledby="delete-tag-title"
            aria-describedby="delete-tag-desc"
            className="relative w-full max-w-md rounded-2xl border bg-white p-6 shadow-xl"
            style={{ borderColor: "#DFE5ED" }}
          >
            <h3 id="delete-tag-title" className="text-sm font-bold" style={{ color: "#162744" }}>
              Eliminar etiqueta
            </h3>
            <p id="delete-tag-desc" className="mt-2 text-xs opacity-80">
              {(deleteTarget.usageCount ?? 0) > 0
                ? `Esta etiqueta está en uso en ${deleteTarget.usageCount} documento(s). ¿Deseas continuar?`
                : `¿Eliminar la etiqueta "${deleteTarget.name}"?`}
            </p>
            <div className="mt-4 flex justify-end gap-2">
              <button
                type="button"
                className="rounded-xl border px-4 py-2 text-xs font-semibold"
                style={{ borderColor: "#DFE5ED" }}
                disabled={deleting}
                onClick={() => setDeleteTarget(null)}
              >
                Cancelar
              </button>
              <button
                type="button"
                className="rounded-xl px-4 py-2 text-xs font-semibold text-white"
                style={{ background: "#FF4E00" }}
                disabled={deleting}
                onClick={() => void confirmDelete()}
              >
                Eliminar
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
