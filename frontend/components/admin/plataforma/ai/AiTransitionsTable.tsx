"use client";

import { useEffect, useMemo, useState } from "react";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { listAiTransitions, type AiProviderTransitionView, type AiToolCode } from "@/lib/api/admin-plataforma-ai";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { formatFechaHora } from "@/lib/format/date";
import { etiquetaMotivoTransicion, etiquetaProveedor } from "./labels";

/**
 * HU #13512 AC2 — historial paginado de transiciones.
 * HU #13918 — paginación estándar de servidor: «Filas por página» 10/20/30/50 (antes fija en 20) y
 * el tamaño elegido se recuerda en la sesión.
 * Ante 404 del endpoint (aún no en backend), muestra vacío sin romper la sección.
 * Remontar con `key` desde el padre para resetear página / recargar tras cambios.
 */
export function AiTransitionsTable({
  toolCode,
  toolLabel,
}: {
  toolCode: AiToolCode;
  toolLabel: string;
}) {
  const pg = usePaginacion("plataforma.AiTransitionsTable");
  const { page, pageSize } = pg;
  const [status, setStatus] = useState<"loading" | "error" | "ready">("loading");
  const [items, setItems] = useState<AiProviderTransitionView[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [retryTick, setRetryTick] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    // Loading inicial / al cambiar página o reintentar desde handlers (no setState síncrono aquí).
    listAiTransitions(toolCode, page, pageSize, controller.signal)
      .then((data) => {
        if (controller.signal.aborted) return;
        setItems(data.items);
        setTotalCount(data.totalCount);
        setStatus("ready");
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted || (err instanceof DOMException && err.name === "AbortError")) return;
        const httpStatus =
          err && typeof err === "object" && "status" in err ? Number((err as { status: number }).status) : 0;
        if (httpStatus === 404 || httpStatus === 501) {
          setItems([]);
          setTotalCount(0);
          setStatus("ready");
          return;
        }
        setStatus("error");
      });
    return () => controller.abort();
  }, [toolCode, page, pageSize, retryTick]);

  const columns: DataTableColumn<AiProviderTransitionView>[] = useMemo(
    () => [
      {
        key: "occurredAt",
        header: "Fecha",
        render: (row) => formatFechaHora(row.occurredAt),
      },
      {
        key: "from",
        header: "Desde",
        render: (row) => (row.fromProvider ? etiquetaProveedor(row.fromProvider) : "—"),
      },
      {
        key: "to",
        header: "Hacia",
        render: (row) => etiquetaProveedor(row.toProvider),
      },
      {
        key: "reason",
        header: "Motivo",
        render: (row) => etiquetaMotivoTransicion(row.reason),
      },
      {
        key: "detail",
        header: "Detalle",
        render: (row) => (
          <span className="line-clamp-2 max-w-[280px] text-xs" title={row.detail ?? undefined}>
            {row.detail ?? "—"}
          </span>
        ),
      },
    ],
    [],
  );

  return (
    <div className="space-y-2">
      <h3 className="text-xs font-bold text-flit-primary dark:text-white">
        Historial de cambios — {toolLabel}
      </h3>
      <DataTable<AiProviderTransitionView>
        columns={columns}
        rows={items}
        getRowKey={(row) => row.id}
        status={status === "loading" ? "loading" : status === "error" ? "error" : undefined}
        onRetry={() => {
          setStatus("loading");
          setRetryTick((n) => n + 1);
        }}
        errorMessage="No se pudo cargar el historial de transiciones."
        emptyMessage="Aún no hay cambios de proveedor registrados para esta herramienta."
        ariaLabel={`Historial de proveedores de ${toolLabel}`}
        minWidth={720}
        pagination={{
          page,
          pageSize,
          totalCount,
          onPageChange: (next) => {
            setStatus("loading");
            pg.setPage(next);
          },
          onPageSizeChange: (next) => {
            setStatus("loading");
            pg.setPageSize(next);
          },
        }}
      />
    </div>
  );
}
