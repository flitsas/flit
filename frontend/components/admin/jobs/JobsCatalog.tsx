"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import {
  CARDLIST_CELL,
  CARDLIST_CELL_CLICKABLE,
  CARDLIST_HEAD_ROW,
  CARDLIST_ROW,
  CARDLIST_SCROLL,
  CARDLIST_TABLE,
  CARDLIST_TH,
} from "@/components/atom/table-cardlist";
import { fetchIctJobCatalog } from "@/lib/api/admin-ict-job-catalog";
import { fetchIctJobSettings } from "@/lib/api/admin-ict-job-settings";
import { fetchQuipuxSettings } from "@/lib/api/admin-quipux-settings";
import { composeUnifiedJobs, type UnifiedJobRow } from "@/lib/admin/compose-unified-jobs";

export function JobsCatalog() {
  const router = useRouter();
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState(false);
  const [rows, setRows] = useState<UnifiedJobRow[]>([]);

  useEffect(() => {
    const controller = new AbortController();
    void Promise.all([
      fetchIctJobCatalog(controller.signal),
      fetchIctJobSettings(controller.signal),
      fetchQuipuxSettings(controller.signal),
    ])
      .then(([ict, settings, quipux]) => {
        if (controller.signal.aborted) return;
        setRows(composeUnifiedJobs(ict, settings, quipux));
        setLoading(false);
      })
      .catch(() => {
        if (controller.signal.aborted) return;
        setLoadError(true);
        setLoading(false);
      });
    return () => controller.abort();
  }, []);

  if (loading) {
    return (
      <div
        className="flex items-center justify-center py-16"
        role="status"
        aria-busy="true"
        aria-live="polite"
      >
        <span className="sr-only">Cargando catálogo de procesos periódicos…</span>
        <div
          className="h-10 w-10 animate-spin rounded-full border-2 border-t-transparent"
          style={{ borderColor: "#557EFF", borderTopColor: "transparent" }}
          aria-hidden="true"
        />
      </div>
    );
  }

  if (loadError) {
    return (
      <p role="alert" className="text-sm" style={{ color: "#FF4E00" }}>
        No se pudo cargar el catálogo de procesos periódicos. Recarga la página para reintentar.
      </p>
    );
  }

  if (rows.length === 0) {
    return (
      <p role="status" className="text-sm opacity-70">
        No hay procesos periódicos para mostrar.
      </p>
    );
  }

  return (
    <div className="space-y-4">
      <aside
        className="rounded-xl px-3 py-2 text-[11px]"
        style={{ background: "#EEF3FF", color: "#1E3A8A", border: "1px solid #C5D4FF" }}
      >
        <p>
          Las consultas RUNT del pre-trámite (familia/placa) las gobierna el job{" "}
          <strong>Orchestrator</strong> de ICT, no un Lambda. La Confirmación RUNT
          post-aprobación vive en{" "}
          <a
            href="/admin/plataforma/confirmacion-runt"
            className="font-semibold underline"
            style={{ color: "#557EFF" }}
          >
            /admin/plataforma/confirmacion-runt
          </a>{" "}
          y no forma parte de este catálogo.
        </p>
      </aside>

      <div className={CARDLIST_SCROLL}>
        <table className={CARDLIST_TABLE}>
          <thead>
            <tr className={CARDLIST_HEAD_ROW}>
              <th className={CARDLIST_TH}>Proceso</th>
              <th className={CARDLIST_TH}>Dueño</th>
              <th className={CARDLIST_TH}>Tipología</th>
              <th className={CARDLIST_TH}>Estado</th>
              <th className={CARDLIST_TH}>Intervalo</th>
              <th className={CARDLIST_TH}>Último run</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.key} className={CARDLIST_ROW}>
                <td className={`${CARDLIST_CELL} ${CARDLIST_CELL_CLICKABLE}`}>
                  <button
                    type="button"
                    className="text-left font-semibold underline"
                    style={{ color: "#557EFF" }}
                    onClick={() => router.push(row.href)}
                  >
                    {row.displayName}
                  </button>
                </td>
                <td className={CARDLIST_CELL}>{row.owner}</td>
                <td className={CARDLIST_CELL}>{row.types.join(" · ")}</td>
                <td className={CARDLIST_CELL}>{row.enabledLabel}</td>
                <td className={CARDLIST_CELL}>{row.intervalLabel}</td>
                <td className={CARDLIST_CELL}>{row.lastRunLabel}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
