"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { Settings } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { CreateButton } from "@/components/atom/CreateButton";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { TABLA_CELDA_SECUNDARIA_CLS } from "@/components/atom/table-styles";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { controlCls } from "@/components/operacion/tramites-control-styles";
import { fetchIctJobCatalog } from "@/lib/api/admin-ict-job-catalog";
import { fetchIctJobSettings } from "@/lib/api/admin-ict-job-settings";
import { fetchQuipuxSettings } from "@/lib/api/admin-quipux-settings";
import {
  composeUnifiedJobs,
  type JobModule,
  type UnifiedJobRow,
} from "@/lib/admin/compose-unified-jobs";

type ModuleFilter = "all" | JobModule;

const FILTERS: { id: ModuleFilter; label: string }[] = [
  { id: "all", label: "Todos" },
  { id: "ICT", label: "ICT" },
  { id: "Quipux", label: "Quipux" },
];

export function JobsCatalog() {
  const router = useRouter();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [rows, setRows] = useState<UnifiedJobRow[]>([]);
  const [filter, setFilter] = useState<ModuleFilter>("all");
  // HU #13918 — paginación estándar en cliente: el catálogo llega completo.
  const pg = usePaginacion("jobs.JobsCatalog");

  const load = useCallback(async (signal?: AbortSignal) => {
    setStatus("loading");
    try {
      const [ict, settings, quipux] = await Promise.all([
        fetchIctJobCatalog(signal),
        fetchIctJobSettings(signal),
        fetchQuipuxSettings(signal),
      ]);
      if (signal?.aborted) return;
      const next = composeUnifiedJobs(ict, settings, quipux);
      setRows(next);
      setStatus(next.length === 0 ? "empty" : "ready");
    } catch {
      if (signal?.aborted) return;
      setStatus("error");
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    // Carga inicial: el setState de `load` ocurre tras el await (no es setState síncrono).
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const visible = useMemo(
    () => (filter === "all" ? rows : rows.filter((r) => r.module === filter)),
    [filter, rows],
  );

  return (
    <div className="flex flex-col gap-4">
      <InlineAlert tone="info" title="Consultas RUNT y Confirmación RUNT no son lo mismo">
        El proceso <strong>Consultas RUNT</strong> (ICT) pide familia/placa en el pre-trámite. La
        Confirmación RUNT post-aprobación vive en{" "}
        <a
          href="/admin/plataforma/confirmacion-runt"
          className="font-semibold underline"
          style={{ color: "var(--color-flit-brand)" }}
        >
          Confirmación RUNT
        </a>{" "}
        y no se configura aquí.
      </InlineAlert>

      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2" role="toolbar" aria-label="Filtrar por módulo">
          {FILTERS.map((item) => (
            <button
              key={item.id}
              type="button"
              className={controlCls(filter === item.id)}
              aria-pressed={filter === item.id}
              onClick={() => {
                setFilter(item.id);
                pg.setPage(1);
              }}
            >
              {item.label}
            </button>
          ))}
        </div>
        <div className="flex flex-wrap items-center gap-2" role="group" aria-label="Configurar cadencia">
          {filter !== "Quipux" ? (
            <CreateButton
              label="Configurar cadencia ICT"
              icon={Settings}
              onClick={() => router.push("/admin/jobs/ict")}
            />
          ) : null}
          {filter !== "ICT" ? (
            <CreateButton
              label="Configurar Quipux"
              icon={Settings}
              onClick={() => router.push("/admin/quipux")}
            />
          ) : null}
        </div>
      </div>

      {status === "loading" ? (
        <CarLoaderModal label="Cargando procesos periódicos…" />
      ) : (
        <UiStateBoundary
          status={status === "ready" && visible.length === 0 ? "empty" : status}
          emptyMessage="No hay procesos periódicos para mostrar en este filtro."
          errorMessage="No se pudo cargar el catálogo de procesos periódicos."
          onRetry={() => void load()}
        >
          <DataTable<UnifiedJobRow>
            ariaLabel="Procesos periódicos"
            columns={JOB_COLUMNS}
            rows={pg.paginar(visible)}
            getRowKey={(row) => row.key}
            pagination={{
              page: pg.page,
              pageSize: pg.pageSize,
              totalCount: visible.length,
              onPageChange: pg.setPage,
              onPageSizeChange: pg.setPageSize,
            }}
          />
        </UiStateBoundary>
      )}
    </div>
  );
}

const JOB_COLUMNS: DataTableColumn<UnifiedJobRow>[] = [
  {
    key: "proceso",
    header: "Proceso",
    render: (row) => (
      <>
        <span className="block font-semibold text-flit-primary dark:text-white">{row.displayName}</span>
        <span className={`mt-0.5 block ${TABLA_CELDA_SECUNDARIA_CLS}`}>{row.technicalName}</span>
      </>
    ),
  },
  {
    key: "modulo",
    header: "Módulo",
    render: (row) => (
      <StatusBadge
        label={row.module}
        tone={row.module === "ICT" ? "info" : "neutral"}
        ariaLabel={`Módulo ${row.module}`}
      />
    ),
  },
  {
    key: "que-hace",
    header: "Qué hace",
    render: (row) => (
      <span className="flex flex-wrap gap-1">
        {row.typeLabels.map((label) => (
          <StatusBadge key={label} label={label} tone="neutral" />
        ))}
      </span>
    ),
  },
  {
    key: "estado",
    header: "Estado",
    render: (row) => <StatusBadge label={row.enabledLabel} tone={row.enabledTone} />,
  },
  {
    key: "cadencia",
    header: "Cadencia",
    cellClassName: "font-medium",
    render: (row) => row.intervalLabel,
  },
  {
    key: "ultimo-run",
    header: "Último run",
    render: (row) => (
      <span className="flex min-w-0 flex-col items-start gap-0.5">
        <StatusBadge label={row.lastRunPrimary} tone={row.lastRunTone} />
        {row.lastRunSecondary ? <span className={TABLA_CELDA_SECUNDARIA_CLS}>{row.lastRunSecondary}</span> : null}
      </span>
    ),
  },
];
