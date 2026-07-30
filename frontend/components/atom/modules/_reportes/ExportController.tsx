"use client";

import { useCallback, useEffect, useState } from "react";
import { Download, Loader2 } from "lucide-react";
import {
  getExport,
  getExportDownloadUrl,
  listExports,
  requestExport,
  type ExportJob,
} from "@/lib/api/reporting-v2";

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

  const refresh = useCallback(async () => {
    try {
      const res = await listExports();
      setJobs(res.items.slice(0, 5));
    } catch {
      /* silencioso en panel secundario */
    }
  }, []);

  useEffect(() => {
    void refresh();
    const id = window.setInterval(() => void refresh(), 5000);
    return () => window.clearInterval(id);
  }, [refresh]);

  const start = async (format: "excel" | "csv" | "pdf") => {
    setBusy(true);
    setError(null);
    try {
      const job = await requestExport({
        reportType,
        format,
        filters: { from, to, tenantId },
      });
      setJobs((prev) => [job, ...prev].slice(0, 5));
      void pollUntilDone(job.id);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "No se pudo solicitar la exportación");
    } finally {
      setBusy(false);
    }
  };

  const pollUntilDone = async (id: string) => {
    for (let i = 0; i < 60; i++) {
      await new Promise((r) => setTimeout(r, 2000));
      try {
        const job = await getExport(id);
        setJobs((prev) => prev.map((j) => (j.id === id ? job : j)));
        if (job.status === "completed" || job.status === "failed") return;
      } catch {
        return;
      }
    }
  };

  const download = async (id: string) => {
    const { downloadUrl } = await getExportDownloadUrl(id);
    window.open(downloadUrl, "_blank", "noopener,noreferrer");
  };

  return (
    <div className="rounded-xl border p-3 space-y-2" aria-live="polite">
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
            Exportar {format.toUpperCase()}
          </button>
        ))}
      </div>
      {error && <p className="text-xs text-red-600">{error}</p>}
      {jobs.length > 0 && (
        <ul className="space-y-1 text-[11px]">
          {jobs.map((job) => (
            <li key={job.id} className="flex items-center justify-between gap-2">
              <span>
                {job.format} · {job.status} · {job.progressPct}%
              </span>
              {job.status === "completed" && (
                <button type="button" className="underline" onClick={() => void download(job.id)}>
                  Descargar
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
