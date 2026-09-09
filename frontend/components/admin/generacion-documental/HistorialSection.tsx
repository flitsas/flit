"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import {
  fetchStandaloneDocuments,
  requestStandaloneDocumentDownload,
} from "@/lib/api/admin-generacion-documental";
import { ApiError } from "@/lib/api/types";
import type { StandaloneDocumentListItem } from "@/lib/api/types-generacion-documental";
import { HistorialTable } from "./HistorialTable";
import {
  HISTORIAL_FILTERS_EMPTY,
  HistorialFilters,
  hasHistorialFilters,
  type HistorialFiltersValue,
  type HistorialUserOption,
} from "./HistorialFilters";
import {
  GENERACION_DOCUMENTAL_BASE_PATH,
  generacionDocumentalTabPath,
} from "./generacion-documental-nav";
import { standaloneDocumentStatusQuery } from "./status-labels";

const PAGE_SIZE = 20;

/**
 * Vista del historial del módulo "Generación documental" (HU-03; CF-17, CF-18, CF-19, CF-22).
 *
 * <p>Resuelve los cuatro estados de UI (CF-22): cargando, error con reintento, vacío CON
 * acción —para generar el primer documento, o para limpiar los filtros si el vacío lo
 * produjo un filtro— y lleno con la tabla. Nunca una tabla vacía sin explicación.</p>
 *
 * <p>La redescarga (CF-19) pide una presigned URL y abre el PDF. <b>La URL no se escribe en
 * consola ni en ningún log</b>: es una credencial de lectura del binario. Un `409` significa
 * que el documento no está generado y un `404` que no existe o no es de esta compañía; en
 * ambos casos se informa sin exponer detalle del documento.</p>
 */
export function HistorialSection() {
  const router = useRouter();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [rows, setRows] = useState<StandaloneDocumentListItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [filters, setFilters] = useState<HistorialFiltersValue>(HISTORIAL_FILTERS_EMPTY);
  const [downloadingId, setDownloadingId] = useState<string | null>(null);
  const [downloadError, setDownloadError] = useState<string | null>(null);

  // Autores ya vistos en esta sesión. Se acumulan para que la opción elegida no desaparezca
  // del selector cuando el propio filtro reduce las filas a las de ese usuario.
  const seenUsers = useRef(new Map<string, string>());
  const [userOptions, setUserOptions] = useState<HistorialUserOption[]>([]);

  const filtered = hasHistorialFilters(filters);

  const load = useCallback(
    async (targetPage: number, current: HistorialFiltersValue, signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const result = await fetchStandaloneDocuments(
          {
            page: targetPage,
            pageSize: PAGE_SIZE,
            documentType: current.documentType
              ? (current.documentType as StandaloneDocumentListItem["documentType"])
              : undefined,
            // CF-21: «En proceso» se expande a los DOS estados internos que cubre.
            status: standaloneDocumentStatusQuery(current.status),
            dateFrom: current.dateFrom || undefined,
            dateTo: current.dateTo || undefined,
            userId: current.userId || undefined,
            // CF-18 en I3: el lote es un filtro mas, en AND con los cuatro anteriores.
            batchId: current.batchId || undefined,
          },
          signal,
        );
        if (signal?.aborted) {
          return;
        }
        const items = result.items ?? [];
        setRows(items);
        setTotalCount(result.total ?? 0);
        setPage(result.page ?? targetPage);
        setStatus(items.length === 0 ? "empty" : "ready");

        let added = false;
        for (const item of items) {
          if (item.createdByUserId && !seenUsers.current.has(item.createdByUserId)) {
            seenUsers.current.set(item.createdByUserId, item.createdByUserName ?? "Usuario sin nombre");
            added = true;
          }
        }
        if (added) {
          setUserOptions([...seenUsers.current].map(([id, name]) => ({ id, name })));
        }
      } catch {
        if (!signal?.aborted) {
          setStatus("error");
        }
      }
    },
    [],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- recarga al cambiar página o filtros
    void load(page, filters, controller.signal);
    return () => controller.abort();
  }, [load, page, filters]);

  const applyFilters = useCallback((value: HistorialFiltersValue) => {
    setDownloadError(null);
    // Un filtro nuevo invalida la página en curso: la 3 de un listado de 5 filas no existe.
    setPage(1);
    setFilters(value);
  }, []);

  const handleDownload = useCallback(async (row: StandaloneDocumentListItem) => {
    setDownloadError(null);
    setDownloadingId(row.id);
    try {
      const link = await requestStandaloneDocumentDownload(row.id);
      // La URL firmada se usa y se descarta: no se loguea, no se guarda en estado, no se muestra.
      window.open(link.url, "_blank", "noopener,noreferrer");
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) {
        setDownloadError("El documento aún no está generado, así que no hay PDF para descargar.");
      } else if (error instanceof ApiError && error.status === 404) {
        setDownloadError("No encontramos ese documento en tu compañía.");
      } else {
        setDownloadError("No se pudo obtener el enlace de descarga. Intenta nuevamente.");
      }
    } finally {
      setDownloadingId(null);
    }
  }, []);

  const emptyMessage = useMemo(
    () =>
      filtered
        ? "Ningún documento coincide con los filtros aplicados."
        : "Aún no has generado documentos en esta compañía.",
    [filtered],
  );

  const emptyCta = filtered ? (
    <button
      type="button"
      onClick={() => applyFilters(HISTORIAL_FILTERS_EMPTY)}
      className="rounded-xl px-4 py-2 text-xs font-semibold text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
      style={{ background: "#557EFF" }}
    >
      Limpiar filtros
    </button>
  ) : (
    <button
      type="button"
      onClick={() => router.push(generacionDocumentalTabPath("rues"))}
      className="rounded-xl px-4 py-2 text-xs font-semibold text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
      style={{ background: "#557EFF" }}
    >
      Generar el primer documento
    </button>
  );

  return (
    <div className="flex flex-1 flex-col gap-4">
      <HistorialFilters value={filters} onChange={applyFilters} users={userOptions} />

      {/* Puente al seguimiento del lote (CF-14): la vista de avance es un detalle al que se
          llega desde aqui, no una cuarta pestana del modulo. */}
      {filters.batchId && (
        <button
          type="button"
          onClick={() => router.push(`${GENERACION_DOCUMENTAL_BASE_PATH}/lotes/${filters.batchId}`)}
          className="w-fit rounded-xl border px-3 py-1.5 text-[11px] font-semibold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
          style={{ borderColor: "#DFE5ED", color: "#557EFF" }}
        >
          Ver seguimiento de este lote
        </button>
      )}

      {downloadError && (
        <p role="alert" className="rounded-xl border px-4 py-2 text-xs" style={{ borderColor: "#FF4E00" }}>
          {downloadError}
        </p>
      )}

      <UiStateBoundary
        status={status}
        onRetry={() => void load(page, filters)}
        skeletonRows={5}
        emptyMessage={emptyMessage}
        emptyCta={emptyCta}
        errorMessage="No se pudo cargar el historial de documentos. Intenta nuevamente."
      >
        <HistorialTable
          rows={rows}
          totalCount={totalCount}
          page={page}
          pageSize={PAGE_SIZE}
          onPageChange={setPage}
          onDownload={(row) => void handleDownload(row)}
          downloadingId={downloadingId}
        />
      </UiStateBoundary>
    </div>
  );
}
