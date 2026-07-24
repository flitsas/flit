"use client";

import { useCallback, useEffect, useState } from "react";
import { AlertTriangle, ChevronLeft, ChevronRight, Loader2 } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { Modal } from "@/components/atom/Modal";
import { StatusBadge } from "@/components/atom/StatusBadge";
import {
  deleteDeed,
  fetchDeedDetail,
  fetchDeeds,
  fetchRepresentedCompanies,
  saveDeed,
  type DeedFormInput,
  type DeedItem,
  type DeedSaved,
  type RepresentedCompany,
} from "@/lib/api/admin-deeds";
import { DeedsFormPanel } from "./DeedsFormPanel";
import { companyNames, formatDate, vigenciaStatus } from "./deedsDisplay";

const PAGE_SIZE = 20;

/**
 * Pestaña "Escrituras" (HU #10905, Feature #10852): CRUD paginado de las escrituras (PDF) por
 * compañía, replicando el patrón de Representantes legales / Baúl de Firmas. El PDF se sube DIRECTO a
 * S3 (presigned) y se ve con una presigned URL de vida corta. Las compañías seleccionables se derivan
 * del directorio de representantes legales (distinct por NIT).
 */
export function DeedsTab({ tenantId }: { tenantId: string }) {
  const { show } = useToast();
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [items, setItems] = useState<DeedItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);

  const [companies, setCompanies] = useState<RepresentedCompany[]>([]);
  const [companiesLoading, setCompaniesLoading] = useState(true);

  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<DeedItem | null>(null);
  const [toDelete, setToDelete] = useState<DeedItem | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [viewingId, setViewingId] = useState<string | null>(null);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const result = await fetchDeeds(tenantId, page, PAGE_SIZE, signal);
        if (signal?.aborted) return;
        setItems(result.data);
        setTotalCount(result.totalCount);
        setStatus(result.data.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) setStatus("error");
      }
    },
    [tenantId, page],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API con AbortController
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  useEffect(() => {
    const controller = new AbortController();
    // Catálogo de compañías para el multi-select y para resolver nombres en la tabla.
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga del catálogo con AbortController
    setCompaniesLoading(true);
    fetchRepresentedCompanies(tenantId, controller.signal)
      .then((list) => {
        if (!controller.signal.aborted) {
          setCompanies(list);
          setCompaniesLoading(false);
        }
      })
      .catch(() => {
        if (!controller.signal.aborted) setCompaniesLoading(false);
      });
    return () => controller.abort();
  }, [tenantId]);

  const handleSubmit = (input: DeedFormInput): Promise<DeedSaved> =>
    saveDeed(tenantId, editing ? editing.id : null, input);

  const handleSaved = () => {
    const wasEditing = editing !== null;
    setFormOpen(false);
    setEditing(null);
    show(wasEditing ? "Escritura actualizada." : "Escritura registrada.", "success");
    void load();
  };

  const openCreate = () => {
    setEditing(null);
    setFormOpen(true);
  };

  const openEdit = (item: DeedItem) => {
    setEditing(item);
    setFormOpen(true);
  };

  const confirmDelete = async () => {
    if (!toDelete) return;
    setDeleting(true);
    try {
      await deleteDeed(tenantId, toDelete.id);
      show("Escritura eliminada.", "success");
      setToDelete(null);
      await load();
    } catch {
      show("No se pudo eliminar la escritura.", "error");
    } finally {
      setDeleting(false);
    }
  };

  const handleView = async (item: DeedItem) => {
    setViewingId(item.id);
    try {
      const detail = await fetchDeedDetail(tenantId, item.id);
      if (detail.viewUrl) {
        window.open(detail.viewUrl, "_blank", "noopener,noreferrer");
      } else {
        show("El PDF de esta escritura aún no está disponible.", "error");
      }
    } catch {
      show("No se pudo abrir el PDF de la escritura.", "error");
    } finally {
      setViewingId(null);
    }
  };

  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

  const emptyCta = (
    <button
      type="button"
      className="rounded-xl px-4 py-2 text-xs font-semibold text-white"
      style={{ background: "#557EFF" }}
      onClick={openCreate}
    >
      Cargar primera escritura
    </button>
  );

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between gap-3">
        <p className="max-w-xl text-[11px] opacity-60">
          Carga las escrituras (PDF) de las compañías que la gestora representa, con su descripción,
          vigencia y las compañías a las que aplican. El documento se puede consultar en cualquier momento.
        </p>
        <button
          type="button"
          className="shrink-0 rounded-xl px-4 py-2 text-xs font-semibold text-white"
          style={{ background: "#557EFF" }}
          onClick={openCreate}
        >
          Nueva escritura
        </button>
      </div>

      <UiStateBoundary
        status={status}
        emptyMessage="Esta compañía aún no tiene escrituras cargadas."
        emptyCta={emptyCta}
        errorMessage="No se pudieron cargar las escrituras."
        onRetry={() => void load()}
        skeletonRows={4}
      >
        <div className="flex flex-col">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[820px] border-separate border-spacing-y-2 text-xs">
              <caption className="sr-only">Escrituras de la compañía</caption>
              <thead>
                <tr className="text-left text-[10px] font-semibold uppercase" style={{ color: "#162744" }}>
                  <th scope="col" className="rounded-l-xl px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                    Descripción
                  </th>
                  <th scope="col" className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                    Compañías
                  </th>
                  <th scope="col" className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                    Vigencia
                  </th>
                  <th scope="col" className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                    Estado
                  </th>
                  <th scope="col" className="rounded-r-xl px-4 py-2.5 text-right" style={{ background: "#DFE5ED" }}>
                    Acciones
                  </th>
                </tr>
              </thead>
              <tbody>
                {items.map((item) => {
                  const st = vigenciaStatus(item.vigenciaDesde, item.vigenciaHasta);
                  const names = companyNames(item.representedCompanyIds, companies);
                  return (
                    <tr key={item.id} className="bg-white dark:bg-[#0B0F14]">
                      <td className="rounded-l-xl border-y border-l px-4 py-3 font-semibold">
                        {item.description}
                      </td>
                      <td className="border-y px-4 py-3">
                        <div className="flex flex-wrap gap-1">
                          {names.length === 0 ? (
                            <span className="opacity-50">—</span>
                          ) : (
                            names.map((n, i) => <StatusBadge key={`${item.id}-${i}`} tone="info" label={n} />)
                          )}
                        </div>
                      </td>
                      <td className="border-y px-4 py-3 whitespace-nowrap">
                        {formatDate(item.vigenciaDesde)} – {formatDate(item.vigenciaHasta)}
                      </td>
                      <td className="border-y px-4 py-3">
                        <StatusBadge tone={st.tone} label={st.label} />
                      </td>
                      <td className="rounded-r-xl border-y border-r px-4 py-3 text-right">
                        <div className="flex flex-wrap justify-end gap-1.5">
                          <RowButton
                            label="Ver PDF"
                            busy={viewingId === item.id}
                            onClick={() => void handleView(item)}
                            ariaLabel={`Ver PDF de ${item.description}`}
                          />
                          <RowButton
                            label="Editar"
                            onClick={() => openEdit(item)}
                            ariaLabel={`Editar ${item.description}`}
                          />
                          <RowButton
                            label="Eliminar"
                            danger
                            onClick={() => setToDelete(item)}
                            ariaLabel={`Eliminar ${item.description}`}
                          />
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          <div className="mt-2 flex items-center justify-between pt-1 text-[11px]">
            <p className="opacity-60">{totalCount} escrituras</p>
            <div className="flex items-center gap-2">
              <button
                type="button"
                aria-label="Página anterior"
                disabled={page <= 1}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                className="flex items-center gap-1 rounded-lg border px-2.5 py-1.5 font-medium disabled:opacity-40"
              >
                <ChevronLeft className="h-3.5 w-3.5" /> Anterior
              </button>
              <span className="font-semibold" style={{ color: "#557EFF" }}>
                {page} / {totalPages}
              </span>
              <button
                type="button"
                aria-label="Página siguiente"
                disabled={page >= totalPages}
                onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                className="flex items-center gap-1 rounded-lg border px-2.5 py-1.5 font-medium disabled:opacity-40"
              >
                Siguiente <ChevronRight className="h-3.5 w-3.5" />
              </button>
            </div>
          </div>
        </div>
      </UiStateBoundary>

      <DeedsFormPanel
        open={formOpen}
        editing={editing}
        companies={companies}
        companiesLoading={companiesLoading}
        onClose={() => {
          setFormOpen(false);
          setEditing(null);
        }}
        onSubmit={handleSubmit}
        onSaved={handleSaved}
        onError={(message) => show(message, "error")}
      />

      {toDelete && (
        <Modal
          open
          onClose={() => setToDelete(null)}
          busy={deleting}
          size="sm"
          icon={AlertTriangle}
          iconBg="#FF4E00"
          title="Eliminar escritura"
        >
          <p className="mt-2 text-sm opacity-80">
            Vas a eliminar la escritura <strong>{toDelete.description}</strong> de esta compañía. Esta
            acción no se puede deshacer.
          </p>
          <div className="mt-5 flex gap-3">
            <button
              type="button"
              onClick={() => setToDelete(null)}
              disabled={deleting}
              className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60"
            >
              Cancelar
            </button>
            <button
              type="button"
              onClick={() => void confirmDelete()}
              disabled={deleting}
              className="flex flex-1 items-center justify-center gap-2 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
              style={{ background: "#FF4E00" }}
            >
              {deleting && <Loader2 className="h-4 w-4 animate-spin" />}
              Eliminar
            </button>
          </div>
        </Modal>
      )}
    </div>
  );
}

function RowButton({
  label,
  onClick,
  ariaLabel,
  danger = false,
  busy = false,
}: {
  label: string;
  onClick: () => void;
  ariaLabel?: string;
  danger?: boolean;
  busy?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={busy}
      aria-label={ariaLabel ?? label}
      className="flex items-center gap-1 rounded-lg border px-3 py-1.5 text-[11px] font-semibold disabled:opacity-60"
      style={danger ? { color: "#FF4E00", borderColor: "#f0c38e" } : undefined}
    >
      {busy && <Loader2 className="h-3 w-3 animate-spin" />}
      {label}
    </button>
  );
}
