"use client";

import { AlertTriangle } from "lucide-react";

/** Badge G6 — historial de auditoría no enriquecido (pre-backfill F11076). */
export function HistoryUnavailableBadge({
  message = "Historial no disponible para este trámite",
}: {
  message?: string;
}) {
  return (
    <div
      className="inline-flex items-center gap-2 rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-900 dark:bg-amber-950/40 dark:text-amber-100"
      role="status"
      data-testid="history-unavailable-badge"
    >
      <AlertTriangle className="h-3.5 w-3.5 shrink-0" aria-hidden />
      <span>{message}</span>
    </div>
  );
}
