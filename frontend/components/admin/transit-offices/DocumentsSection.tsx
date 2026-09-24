"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { Tag, Trash2 } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { getToken } from "@/lib/api/client";
import { tramitesClient } from "@/lib/api/tramites-client";
import {
  createOtDocumentTag,
  deleteOtDocumentTag,
  fetchOtDocumentPrecedence,
  fetchOtDocumentTags,
  updateOtDocumentPrecedence,
} from "@/lib/api/admin-ot";
import type { OtDocumentPrecedenceItem, OtDocumentTag } from "@/lib/api/types-ot";
import { decodeJwtPayload, isSuperAdmin } from "@/lib/auth/jwt";
import { PledgeDocumentOverrideToggle } from "@/components/admin/documents/panels/PledgeDocumentOverrideToggle";
import { CreateButton } from "@/components/atom/CreateButton";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { RowActions } from "@/components/atom/RowActions";
// HU #12883 AC1 — trampa de foco de diálogo ya compartida (nace en el wizard, es genérica).
import { useWizardFocusTrap } from "@/components/operacion/use-wizard-focus-trap";
import { DocumentPrecedenceList } from "./DocumentPrecedenceList";
import { OtTabBar } from "./OtTabBar";
import { OT_INPUT_CLS } from "./ot-form-styles";
import { TAG_COLOR_OPTIONS, TagFormPanel } from "./TagFormPanel";

/** Nombre accesible/legible del color de una etiqueta (HU #12883 AC3 — paleta cerrada). */
function tagColorLabel(hex: string): string {
  const found = TAG_COLOR_OPTIONS.find((opt) => opt.hex.toLowerCase() === hex.toLowerCase());
  return found ? `${found.name} (${hex.toUpperCase()})` : hex.toUpperCase();
}

type Tab = "precedence" | "tags";

interface ProcedureTypeOption {
  id: string;
  name: string;
}

export interface DocumentsSectionProps {
  /** OT del hub actual (route param `[id]`); llave del override de obligatoriedad (HU #10887). */
  transitOfficeId: string;
}

/** Prelación documental DnD y CRUD etiquetas OT (HU #10224). */
export function DocumentsSection({ transitOfficeId }: DocumentsSectionProps) {
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
  // HU #12861 (Feature #12848) — el Admin OT ("solo ordena") pierde Etiquetas y el switch de
  // prenda; solo Super Admin conserva ambas pestañas. Mismo helper que OtHubLayout.tsx.
  const [superAdmin, setSuperAdmin] = useState(false);
  // HU #12883 AC1 — trampa de foco + Escape + retorno de foco del diálogo "Eliminar etiqueta".
  const deleteDialogRef = useRef<HTMLDivElement>(null);
  useWizardFocusTrap(deleteDialogRef, {
    active: superAdmin && deleteTarget !== null,
    onEscape: () => {
      if (!deleting) setDeleteTarget(null);
    },
  });

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- lee el rol una sola vez al montar
    setSuperAdmin(isSuperAdmin(decodeJwtPayload(getToken())));
  }, []);

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
        const result = await fetchOtDocumentPrecedence(procedureTypeId, signal, { transitOfficeId });
        if (signal?.aborted) return;
        setPrecedence(result.data);
        setPrecStatus(result.data.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) setPrecStatus("error");
      }
    },
    [procedureTypeId, transitOfficeId],
  );

  const loadTags = useCallback(
    async (signal?: AbortSignal) => {
      setTagStatus("loading");
      try {
        const result = await fetchOtDocumentTags(signal, { transitOfficeId });
        if (signal?.aborted) return;
        setTags(result.data);
        setTagStatus(result.data.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) setTagStatus("error");
      }
    },
    [transitOfficeId],
  );

  useEffect(() => {
    if (tab !== "precedence") return;
    const c = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga bajo demanda al cambiar pestaña
    void loadPrecedence(c.signal);
    return () => c.abort();
  }, [tab, loadPrecedence]);

  useEffect(() => {
    if (tab !== "tags") return;
    const c = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga bajo demanda al cambiar pestaña
    void loadTags(c.signal);
    return () => c.abort();
  }, [tab, loadTags]);

  const handleReorder = async (items: OtDocumentPrecedenceItem[]) => {
    try {
      const result = await updateOtDocumentPrecedence(
        {
          procedure_type_id: procedureTypeId,
          items: items.map((i) => ({
            document_type_id: i.document_type_id,
            sort_order: i.sort_order,
          })),
        },
        { transitOfficeId },
      );
      setPrecedence(result.data);
      // HU #11185 AC4 — reordenar no rehace los expedientes ya emitidos (decisión D6): el
      // organismo tiene que saber desde cuándo aplica lo que acaba de guardar.
      show("Orden guardado. Aplica a partir de la próxima generación del expediente.", "success");
    } catch (error) {
      // AC5 — se informa y la lista vuelve al orden anterior (el rollback lo hace la lista).
      show("No se pudo guardar el orden. Se restauró el anterior.", "error");
      throw error;
    }
  };

  const confirmDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await deleteOtDocumentTag(deleteTarget.id, { transitOfficeId });
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

  // HU #12883 AC3 — Etiquetas pasa de píldoras sueltas a tabla semántica (mismo patrón que
  // OtMandatosSection): <table>/<thead>/<th scope> reales vía DataTable, no una grilla de <div>.
  const tagColumns: DataTableColumn<OtDocumentTag>[] = [
    {
      key: "name",
      header: "Etiqueta",
      cellClassName: "font-semibold",
      render: (tag) => tag.name,
    },
    {
      key: "color",
      header: "Color",
      render: (tag) => (
        <span className="inline-flex items-center gap-2">
          <span
            aria-hidden="true"
            className="h-4 w-4 shrink-0 rounded-full border border-black/10"
            style={{ background: tag.color }}
          />
          <span className="text-xs">{tagColorLabel(tag.color)}</span>
        </span>
      ),
    },
    {
      key: "actions",
      header: "Acción",
      align: "right",
      render: (tag) => (
        <RowActions
          actions={[
            {
              icon: Trash2,
              label: `Eliminar etiqueta ${tag.name}`,
              tone: "danger",
              onClick: () => setDeleteTarget(tag),
            },
          ]}
        />
      ),
    },
  ];

  return (
    <div className="space-y-4">
      {/* HU #12861 AC1 (Feature #12848) — ot_admin ("solo ordena") no ve selector de pestañas:
          Etiquetas no existe para su rol, así que ofrecerlo aquí sería un enlace muerto. */}
      {superAdmin && (
        <OtTabBar
          ariaLabel="Secciones documentales"
          tabs={[
            { id: "precedence", label: "Prelación" },
            { id: "tags", label: "Etiquetas" },
          ]}
          activeId={tab}
          onChange={(id) => setTab(id as Tab)}
        />
      )}

      {tab === "precedence" && (
        <div role="tabpanel" className="space-y-4 pt-2">
          {/* HU #12883 AC2 — mismo patrón de encabezado que OtMandatosSection: h2 navy + descripción
              legible; el selector de "Tipo de trámite" queda alineado a la derecha, con ancho
              acotado (como el buscador de Mandatos) para que la descripción use el resto del
              ancho disponible y no quede en una columna angosta con un hueco al lado. */}
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="min-w-0 flex-1">
              <h2 className="text-sm font-semibold text-[#162744] dark:text-white">
                Orden de documentos del expediente
              </h2>
              <p className="mt-1 text-xs leading-relaxed text-[#59677D] dark:text-white/65">
                Arrastra un documento —o usa las flechas y Enter— hasta la página que quieres que
                ocupe. El orden nuevo aplica a partir de la próxima generación del expediente.
              </p>
            </div>
            <label className="w-full shrink-0 text-xs font-semibold text-foreground sm:w-80">
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
          </div>

          {/* HU #12861 AC2 — el switch de prenda queda exclusivo de Super Admin; para ot_admin
              ni siquiera se monta (evita la llamada a la API de políticas de prenda). */}
          {superAdmin && (
            <section className="space-y-3 rounded-2xl border bg-card p-4">
              <h3 className="text-sm font-semibold text-[#162744] dark:text-white">
                Documento de prenda por compañía
              </h3>
              <PledgeDocumentOverrideToggle transitOfficeId={transitOfficeId} />
            </section>
          )}

          <UiStateBoundary
            status={precStatus}
            emptyMessage="Este tipo de trámite no tiene documentos asociados todavía."
            errorMessage="Error al cargar la prelación."
            onRetry={() => void loadPrecedence()}
            skeletonRows={4}
          >
            <DocumentPrecedenceList items={precedence} onReorder={handleReorder} />
          </UiStateBoundary>
        </div>
      )}

      {superAdmin && tab === "tags" && (
        <div role="tabpanel" className="space-y-3 pt-2">
          <div className="flex justify-end">
            <CreateButton
              label="Nueva etiqueta"
              icon={Tag}
              onClick={() => setTagFormOpen(true)}
            />
          </div>

          <UiStateBoundary
            status={tagStatus}
            emptyMessage="No hay etiquetas configuradas."
            emptyCta={
              <CreateButton
                label="Crear primera etiqueta"
                icon={Tag}
                onClick={() => setTagFormOpen(true)}
              />
            }
            errorMessage="Error al cargar etiquetas."
            onRetry={() => void loadTags()}
            skeletonRows={3}
          >
            <DataTable
              columns={tagColumns}
              rows={tags}
              getRowKey={(tag) => tag.id}
              ariaLabel="Etiquetas documentales"
              allowHorizontalScroll={false}
            />
          </UiStateBoundary>
        </div>
      )}

      {superAdmin && (
        <TagFormPanel
          open={tagFormOpen}
          onClose={() => setTagFormOpen(false)}
          onCreate={(body) => createOtDocumentTag(body, { transitOfficeId })}
          onSaved={(tag) => {
            setTags((prev) => [...prev, tag]);
            setTagStatus("ready");
            setTagFormOpen(false);
            show("Etiqueta creada.", "success");
          }}
        />
      )}

      {superAdmin && deleteTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          {/* HU #12883 AC1 — overlay exacto del token FLIT (rgba(22,39,68,0.45) + blur 6px);
              antes era `bg-slate-900/40`, ajeno a la paleta FLIT. */}
          <button
            type="button"
            className="absolute inset-0"
            style={{ background: "rgba(22,39,68,0.45)", backdropFilter: "blur(6px)" }}
            aria-label="Cerrar"
            onClick={() => !deleting && setDeleteTarget(null)}
          />
          <div
            ref={deleteDialogRef}
            role="alertdialog"
            aria-labelledby="delete-tag-title"
            aria-describedby="delete-tag-desc"
            className="relative w-full max-w-md max-h-[90dvh] overflow-y-auto rounded-2xl border bg-card p-6 shadow-xl"
          >
            <h3 id="delete-tag-title" className="text-sm font-bold text-foreground">
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
