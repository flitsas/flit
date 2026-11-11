"use client";

// HU #14076 AC3 — Historial de versiones publicadas: cuándo se publicó cada una, quién y cuánto contenía. Solo lectura.
import { useCallback, useEffect, useState } from "react";
import { History } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { Modal } from "@/components/atom/Modal";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { formatFechaHora } from "@/lib/format/date";
import { fetchVersionesPn, type ResumenVersionContenidoPn } from "@/lib/api/admin-pn-contenido";

const columns: DataTableColumn<ResumenVersionContenidoPn>[] = [
  { key: "version", header: "N.º", cellClassName: "font-semibold", render: (v) => v.version },
  { key: "fecha", header: "Publicada el", cellClassName: "whitespace-nowrap", render: (v) => formatFechaHora(v.publicadoEn) },
  { key: "preguntas", header: "Preguntas", render: (v) => v.preguntas },
  { key: "banners", header: "Banners", render: (v) => v.banners },
];

export function VersionesModal({ onClose }: { onClose: () => void }) {
  const [versiones, setVersiones] = useState<ResumenVersionContenidoPn[]>([]);
  const [status, setStatus] = useState<UiStatus>("loading");
  const pg = usePaginacion("pn.contenido.versiones");

  const load = useCallback(async (signal?: AbortSignal) => {
    setStatus("loading");
    try {
      const list = await fetchVersionesPn(signal);
      if (signal?.aborted) return;
      setVersiones(list);
      setStatus(list.length === 0 ? "empty" : "ready");
    } catch {
      if (!signal?.aborted) setStatus("error");
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial: skeleton intencional
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  return (
    <Modal open onClose={onClose} title="Publicaciones anteriores" description="Cada vez que publicas queda aquí, de la más reciente a la más antigua." icon={History} size="lg">
      {status === "loading" ? (
        <CarLoaderModal label="Cargando publicaciones…" />
      ) : (
        <UiStateBoundary
          status={status}
          emptyMessage="Todavía no has publicado nada."
          errorMessage="No se pudieron cargar las publicaciones anteriores."
          onRetry={() => void load()}
        >
          <DataTable<ResumenVersionContenidoPn>
            ariaLabel="Publicaciones anteriores del contenido"
            columns={columns}
            rows={pg.paginar(versiones)}
            getRowKey={(v) => String(v.version)}
            pagination={{
              page: pg.page,
              pageSize: pg.pageSize,
              totalCount: versiones.length,
              onPageChange: pg.setPage,
              onPageSizeChange: pg.setPageSize,
            }}
          />
        </UiStateBoundary>
      )}
    </Modal>
  );
}
