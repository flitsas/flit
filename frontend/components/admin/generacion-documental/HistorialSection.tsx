"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { fetchStandaloneDocuments } from "@/lib/api/admin-generacion-documental";
import type { StandaloneDocumentListItem } from "@/lib/api/types-generacion-documental";
import { HistorialTable } from "./HistorialTable";
import { generacionDocumentalTabPath } from "./generacion-documental-nav";

const PAGE_SIZE = 20;

/**
 * Vista del historial del módulo "Generación documental" (HU-01, shell).
 *
 * Resuelve los cuatro estados de UI (CF-22): cargando (skeleton de `UiStateBoundary`),
 * error con reintento, vacío CON acción para generar el primer documento —no una tabla
 * vacía sin explicación— y lleno con la tabla.
 *
 * Los filtros por tipo, rango de fecha y usuario, y la redescarga presignada, son HU-03:
 * aquí el listado se pide sin filtros y la columna de acciones queda inerte.
 */
export function HistorialSection() {
  const router = useRouter();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [rows, setRows] = useState<StandaloneDocumentListItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);

  const load = useCallback(async (targetPage: number, signal?: AbortSignal) => {
    setStatus("loading");
    try {
      const result = await fetchStandaloneDocuments({ page: targetPage, pageSize: PAGE_SIZE }, signal);
      if (signal?.aborted) {
        return;
      }
      setRows(result.items ?? []);
      setTotalCount(result.total ?? 0);
      setPage(result.page ?? targetPage);
      setStatus((result.items ?? []).length === 0 ? "empty" : "ready");
    } catch {
      if (!signal?.aborted) {
        setStatus("error");
      }
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- recarga al cambiar de página
    void load(page, controller.signal);
    return () => controller.abort();
  }, [load, page]);

  return (
    <div className="flex flex-1 flex-col gap-4">
      <UiStateBoundary
        status={status}
        onRetry={() => void load(page)}
        skeletonRows={5}
        emptyMessage="Aún no has generado documentos en esta compañía."
        emptyCta={
          <button
            type="button"
            onClick={() => router.push(generacionDocumentalTabPath("rues"))}
            className="rounded-xl px-4 py-2 text-xs font-semibold text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
            style={{ background: "#557EFF" }}
          >
            Generar el primer documento
          </button>
        }
        errorMessage="No se pudo cargar el historial de documentos. Intenta nuevamente."
      >
        <HistorialTable
          rows={rows}
          totalCount={totalCount}
          page={page}
          pageSize={PAGE_SIZE}
          onPageChange={setPage}
        />
      </UiStateBoundary>
    </div>
  );
}
