"use client";

import { DataTable, StatusBadge, usePaginacion, type StatusTone } from "@flit/ui";
import { Encabezado } from "@/components/Pantalla";
import { useToast } from "@/components/Toast";
import { apiDownload, apiFetch } from "@/lib/api";
import { CAMBIO_CONTRAVENTOR, ESTADO_LEGAL, ESTADO_PROCESO, FUENTE, ULTIMO_AVISO } from "@/lib/etiquetas";
import { describirFiltros } from "@/lib/filtros";
import { formatoFechaHora } from "@/lib/format/date";
import type { Exportacion } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

const ESTADOS: Record<string, { texto: string; tono: StatusTone }> = {
  EnCola: { texto: "En cola", tono: "neutral" },
  Generando: { texto: "Generando", tono: "info" },
  Lista: { texto: "Lista", tono: "success" },
  Fallida: { texto: "Falló", tono: "danger" },
  Expirada: { texto: "Vencida", tono: "neutral" },
};

/** Exportaciones grandes de la bandeja que se generaron en segundo plano (§6.20): disponibles 7 días. */
export default function MisExportaciones() {
  const toast = useToast();
  const pg = usePaginacion(10);
  const lista = useCarga("exportaciones", (signal) => apiFetch<Exportacion[]>("/exportaciones", { signal }));
  const filas = lista.datos ?? [];

  const descargar = async (e: Exportacion) => {
    try {
      await apiDownload(`/exportaciones/${e.id}/archivo`, "comparendos.xlsx");
    } catch (err) {
      toast.show(err instanceof Error ? err.message : "No se pudo descargar.", "error");
    }
  };

  return (
    <section className="flex flex-col gap-4">
      <Encabezado titulo="Mis exportaciones" descripcion="Las exportaciones de más de 5.000 filas se generan aquí y quedan disponibles 7 días." />
      <DataTable
        ariaLabel="Exportaciones"
        rows={pg.paginar(filas)}
        getRowKey={(e) => e.id}
        status={lista.estado}
        errorMessage={lista.error ?? undefined}
        onRetry={lista.recargar}
        emptyMessage="Todavía no tienes exportaciones."
        columns={[
          { key: "creada", header: "Pedida", render: (e) => formatoFechaHora(e.creadoEn) },
          {
            key: "filtros",
            header: "Filtros",
            render: (e) => (
              <span className="text-xs">
                {describirFiltros(e.filtros, e.nombres, {
                  estadoLegal: ESTADO_LEGAL,
                  estadoProceso: ESTADO_PROCESO,
                  ultimoAviso: ULTIMO_AVISO,
                  cambioContraventor: CAMBIO_CONTRAVENTOR,
                  fuente: FUENTE,
                })}
              </span>
            ),
          },
          { key: "filas", header: "Filas", align: "right", render: (e) => e.totalFilas?.toLocaleString("es-CO") ?? "—" },
          { key: "estado", header: "Estado", render: (e) => <StatusBadge label={ESTADOS[e.estado]?.texto ?? e.estado} tone={ESTADOS[e.estado]?.tono ?? "neutral"} /> },
          { key: "vence", header: "Disponible hasta", render: (e) => formatoFechaHora(e.expiraEn) },
          {
            key: "accion",
            header: "",
            align: "right",
            render: (e) =>
              e.estado === "Lista" ? (
                <button type="button" className="text-xs font-semibold text-[#3B4FD6] hover:underline" onClick={() => descargar(e)}>
                  Descargar
                </button>
              ) : null,
          },
        ]}
        pagination={{ page: pg.page, pageSize: pg.pageSize, totalCount: filas.length, onPageChange: pg.setPage, onPageSizeChange: pg.setPageSize }}
      />
    </section>
  );
}
