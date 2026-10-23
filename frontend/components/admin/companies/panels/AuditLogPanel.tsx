"use client";

import { useCallback, useEffect, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { AuditLogTable } from "@/components/admin/companies/AuditLogTable";
import { fetchAuditLog } from "@/lib/api/admin-companies";
import type { AuditLogPageResponse } from "@/lib/api/types";

// Slot del historial de auditoría (HU #10194, AC5). Se monta al abrir la pestaña
// "Historial de Cambios" → carga diferida. Paginación server-side.
export function AuditLogPanel({
  tenantId,
  networkHeadId,
}: {
  tenantId: string;
  networkHeadId?: string | null;
}) {
  // Bug #13055 — tabla homologada con el modelo de trámites: filas por página elegibles.
  const { page, pageSize, setPage, setPageSize } = usePaginacion();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [result, setResult] = useState<AuditLogPageResponse | null>(null);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const data = await fetchAuditLog(tenantId, page, pageSize, signal, networkHeadId);
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
    [tenantId, page, pageSize, networkHeadId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // Carga inicial de datos al montar: el skeleton (setStatus loading) es intencional.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  // Primera carga: solo el loader. Al paginar se conserva la tabla montada para no perder los filtros.
  if (status === "loading" && !result) {
    return <CarLoaderModal label="Cargando historial de cambios…" />;
  }

  return (
    <>
    {status === "loading" ? <CarLoaderModal label="Cargando historial de cambios…" /> : null}
    <UiStateBoundary
      status={status === "loading" ? "ready" : status}
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
          onPageSizeChange={setPageSize}
        />
      )}
    </UiStateBoundary>
    </>
  );
}
