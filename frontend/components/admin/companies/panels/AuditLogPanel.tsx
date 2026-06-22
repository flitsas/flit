"use client";

import { useCallback, useEffect, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { AuditLogTable } from "@/components/admin/companies/AuditLogTable";
import { fetchAuditLog } from "@/lib/api/admin-companies";
import type { AuditLogPageResponse } from "@/lib/api/types";

const PAGE_SIZE = 20;

// Slot del historial de auditoría (HU #10194, AC5). Se monta al abrir la pestaña
// "Historial de Cambios" → carga diferida. Paginación server-side.
export function AuditLogPanel({ tenantId }: { tenantId: string }) {
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [result, setResult] = useState<AuditLogPageResponse | null>(null);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const data = await fetchAuditLog(tenantId, page, PAGE_SIZE, signal);
        if (signal?.aborted) {
          return;
        }
        setResult(data);
        setStatus(data.data.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) {
          setStatus("error");
        }
      }
    },
    [tenantId, page],
  );

  useEffect(() => {
    const controller = new AbortController();
    // Carga inicial de datos al montar: el skeleton (setStatus loading) es intencional.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  return (
    <UiStateBoundary
      status={status}
      onRetry={() => void load()}
      emptyMessage="Aún no hay cambios registrados para esta compañía."
      errorMessage="No se pudo cargar el historial de auditoría."
    >
      {result && (
        <AuditLogTable
          entries={result.data}
          totalCount={result.totalCount}
          page={result.page}
          pageSize={result.pageSize}
          onPageChange={setPage}
        />
      )}
    </UiStateBoundary>
  );
}
