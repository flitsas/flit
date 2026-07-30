"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Download, Loader2, X } from "lucide-react";
import {
  getExportDownloadUrl,
  listExports,
  requestExport,
  type ExportJob,
} from "@/lib/api/reporting-v2";
import { watchExportJob } from "@/lib/signalr/export-jobs-client";

type ToastState =
  | { kind: "success"; jobId: string; message: string }
  | { kind: "error"; message: string }
  | null;

function isInProgress(status: string): boolean {
  return status === "pending" || status === "processing";
}

/** Contador de jobs activos para el badge (AC2/AC6 — failed no cuenta). */
export function countPendingExports(jobs: ReadonlyArray<Pick<ExportJob, "status">>): number {
  return jobs.filter((j) => isInProgress(j.status)).length;
}

export function ExportController({
  reportType,
  from,
  to,
  tenantId,
  disabled,
}: {
  reportType: string;
  from: string;
  to: string;
  tenantId?: string;
  disabled?: boolean;
}) {
  const [jobs, setJobs] = useState<ExportJob[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loaded, setLoaded] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);
  const watchers = useRef<Map<string, () => void>>(new Map());

  const pendingCount = useMemo(() => countPendingExports(jobs), [jobs]);

  const refresh = useCallback(async () => {
    try {
      const res = await listExports();
      setJobs(res.items.slice(0, 8));
    } catch {
      /* silencioso en panel secundario */
    } finally {
      setLoaded(true);
    }
  }, []);

  const attachWatcher = useCallback(async (jobId: string) => {
    watchers.current.get(jobId)?.();
    const dispose = await watchExportJob(jobId, {
      onProgress: (event) => {
        setJobs((prev) =>
          prev.map((j) =>
            j.id === event.jobId
              ? { ...j, status: event.status, progressPct: event.progressPct }
              : j,
          ),
        );
      },
      onCompleted: (event) => {
        setJobs((prev) =>
          prev.map((j) =>
            j.id === event.jobId
              ? { ...j, status: event.status, progressPct: event.progressPct }
              : j,
          ),
        );
        setToast({ kind: "success", jobId: event.jobId, message: "Exportación lista" });
        watchers.current.get(jobId)?.();
        watchers.current.delete(jobId);
      },
      onFailed: (event) => {
        setJobs((prev) => {
          const current = prev.find((j) => j.id === event.jobId);
          const msg = current?.errorMessage || "La exportación falló";
          queueMicrotask(() => setToast({ kind: "error", message: msg }));
          return prev.map((j) =>
            j.id === event.jobId
              ? { ...j, status: event.status, progressPct: event.progressPct }
              : j,
          );
        });
        watchers.current.get(jobId)?.();
        watchers.current.delete(jobId);
      },
    });
    watchers.current.set(jobId, dispose);
  }, []);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga async inicial
    void refresh();
    const active = watchers.current;
    return () => {
      for (const dispose of active.values()) dispose();
      active.clear();
    };
  }, [refresh]);

  useEffect(() => {
    for (const job of jobs) {
      if (isInProgress(job.status) && !watchers.current.has(job.id)) {
        void attachWatcher(job.id);
      }
    }
  }, [jobs, attachWatcher]);

  const start = async (format: "excel" | "csv" | "pdf") => {
    setBusy(true);
    setError(null);
    try {
      const job = await requestExport({
        reportType,
        format,
        filters: { from, to, tenantId },
      });
      setJobs((prev) => [job, ...prev].slice(0, 8));
      void attachWatcher(job.id);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "No se pudo solicitar la exportación");
    } finally {
      setBusy(false);
    }
  };

  const download = async (id: string) => {
    const { downloadUrl } = await getExportDownloadUrl(id);
    window.open(downloadUrl, "_blank", "noopener,noreferrer");
  };

  return (
    <div className="rounded-xl border p-3 space-y-2 min-w-[220px]" data-testid="export-controller">
      <div className="flex items-center justify-between gap-2">
        <p className="text-xs font-semibold">Exportaciones</p>
        <span
          data-testid="export-pending-badge"
          className="inline-flex min-w-5 items-center justify-center rounded-full bg-[#557EFF] px-1.5 py-0.5 text-[10px] font-bold text-white"
          aria-label={`${pendingCount} exportaciones en progreso`}
        >
          {pendingCount}
        </span>
      </div>

      <div className="flex flex-wrap gap-2">
        {(["excel", "csv", "pdf"] as const).map((format) => (
          <button
            key={format}
            type="button"
            disabled={disabled || busy}
            onClick={() => void start(format)}
            className="inline-flex items-center gap-1 rounded-lg border px-3 py-1.5 text-xs font-medium disabled:opacity-50"
          >
            {busy ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Download className="h-3.5 w-3.5" />}
            {format.toUpperCase()}
          </button>
        ))}
      </div>

      {error && <p className="text-xs text-red-600">{error}</p>}

      {loaded && jobs.length === 0 && (
        <p className="text-[11px] opacity-70" data-testid="export-empty">
          Sin exportaciones recientes
        </p>
      )}

      {jobs.length > 0 && (
        <ul className="space-y-2 text-[11px]" aria-live="polite">
          {jobs.map((job) => (
            <li key={job.id} className="space-y-1">
              <div className="flex items-center justify-between gap-2">
                <span>
                  {job.format} · {job.status}
                </span>
                {job.status === "completed" && (
                  <button
                    type="button"
                    className="underline"
                    onClick={() => void download(job.id)}
                  >
                    Descargar
                  </button>
                )}
              </div>
              {isInProgress(job.status) && (
                <div
                  className="h-1.5 w-full overflow-hidden rounded bg-black/10 dark:bg-white/10"
                  role="progressbar"
                  aria-valuemin={0}
                  aria-valuemax={100}
                  aria-valuenow={job.progressPct}
                  aria-label={`Progreso ${job.progressPct}%`}
                  data-testid={`export-progress-${job.id}`}
                >
                  <div
                    className="h-full bg-[#557EFF] transition-[width]"
                    style={{ width: `${Math.min(100, Math.max(0, job.progressPct))}%` }}
                  />
                </div>
              )}
              {isInProgress(job.status) && (
                <span className="opacity-70">{job.progressPct}%</span>
              )}
            </li>
          ))}
        </ul>
      )}

      {toast && (
        <div
          role="status"
          data-testid={toast.kind === "success" ? "export-toast-success" : "export-toast-error"}
          className={`flex items-start justify-between gap-2 rounded-lg border px-2 py-1.5 text-[11px] ${
            toast.kind === "error"
              ? "border-red-300 bg-red-50 text-red-800 dark:bg-red-950/40 dark:text-red-200"
              : "border-emerald-300 bg-emerald-50 text-emerald-900 dark:bg-emerald-950/40 dark:text-emerald-100"
          }`}
        >
          <div className="space-y-1">
            <p className="font-semibold">{toast.message}</p>
            {toast.kind === "success" && (
              <button
                type="button"
                className="underline font-medium"
                onClick={() => void download(toast.jobId)}
              >
                Descargar
              </button>
            )}
          </div>
          <button
            type="button"
            aria-label="Cerrar aviso"
            onClick={() => setToast(null)}
            className="opacity-70 hover:opacity-100"
          >
            <X className="h-3.5 w-3.5" />
          </button>
        </div>
      )}
    </div>
  );
}
