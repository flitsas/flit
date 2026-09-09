"use client";

import { useCallback, useEffect, useState } from "react";
import { Pagination } from "@/components/atom/Pagination";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { fetchStandaloneBatchItems } from "@/lib/api/admin-generacion-documental";
import type { StandaloneBatchItem } from "@/lib/api/types-generacion-documental";
import { generacionDocumentalTypeLabel } from "./generacion-documental-nav";
import { standaloneDocumentStatusView } from "./status-labels";

const PAGE_SIZE = 20;

export interface BatchItemsTableProps {
  batchId: string;
  /**
   * Cambia cuando el lote avanza. Sirve para recargar las filas al ritmo del seguimiento sin
   * montar un segundo temporizador: el polling ya vive en `useBatchPolling` y basta con uno.
   */
  refreshKey?: number;
}

/**
 * Tabla de filas de un lote (CF-13 en la interfaz, HU #12211).
 *
 * <p><b>Qué se muestra por fila:</b> número de fila del XLSX, tipo, estado y —cuando falló— el
 * <b>código y el campo</b> del error. <b>Nunca el valor capturado</b> que lo produjo: la fila puede
 * traer una cédula, una placa o una dirección, y el listado se los enseñaría a cualquiera con
 * permiso de lectura. El contrato del backend tampoco lo trae; aquí no hay nada que ocultar
 * después.</p>
 *
 * <p><b>Trampa del esquema (heredada de HU #12210):</b> una fila cuyo `document_type` no se pudo
 * tipificar —tipo desconocido, o transferencia sin escenario válido— se persiste como
 * `certificado_rues` porque los CHECK de la tabla solo admiten dos literales. En esas filas la
 * columna MIENTE, así que cuando hay `validationErrors` se muestra el error, no el tipo: es lo
 * único verdadero que quedó de esa fila. No es un bug que se arregle aquí — corregirlo exige un
 * DDL nuevo y es decisión del PO.</p>
 */
export function BatchItemsTable({ batchId, refreshKey = 0 }: BatchItemsTableProps) {
  const [rows, setRows] = useState<StandaloneBatchItem[]>([]);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [page, setPage] = useState(1);
  const [totalCount, setTotalCount] = useState(0);

  const load = useCallback(
    async (targetPage: number, signal?: AbortSignal) => {
      try {
        const result = await fetchStandaloneBatchItems(
          batchId,
          { page: targetPage, pageSize: PAGE_SIZE },
          signal,
        );
        if (signal?.aborted) {
          return;
        }
        const items = result.items ?? [];
        setRows(items);
        setTotalCount(result.total ?? 0);
        setStatus(items.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) {
          setStatus("error");
        }
      }
    },
    [batchId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- recarga al cambiar de página o al avanzar el lote
    void load(page, controller.signal);
    return () => controller.abort();
  }, [load, page, refreshKey]);

  return (
    <section aria-labelledby="lote-items-titulo" className="flex flex-1 flex-col gap-3">
      <h2 id="lote-items-titulo" className="text-xs font-semibold uppercase opacity-70">
        Filas del lote
      </h2>

      <UiStateBoundary
        status={status}
        onRetry={() => void load(page)}
        skeletonRows={5}
        emptyMessage="Todavía no hay filas procesadas de este lote."
        errorMessage="No se pudieron cargar las filas del lote. Intenta nuevamente."
      >
        <div className="overflow-x-auto">
          <table className="w-full min-w-[720px] border-separate border-spacing-y-2 text-xs">
            <caption className="sr-only">Filas del lote con su resultado y el detalle del error</caption>
            <thead>
              <tr className="text-left text-[10px] font-semibold uppercase" style={{ color: "#162744" }}>
                <th className="rounded-l-xl px-4 py-2.5" style={{ background: "#DFE5ED" }} scope="col">
                  Fila
                </th>
                <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }} scope="col">
                  Tipo
                </th>
                <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }} scope="col">
                  Estado
                </th>
                <th className="rounded-r-xl px-4 py-2.5" style={{ background: "#DFE5ED" }} scope="col">
                  Detalle del error
                </th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => {
                const vista = standaloneDocumentStatusView(row.status);
                const errores = row.validationErrors ?? [];
                return (
                  <tr key={row.id} className="bg-white dark:bg-[#0B0F14]">
                    <td className="rounded-l-xl border-y border-l px-4 py-3 font-medium">
                      {row.rowNumber ?? "—"}
                    </td>
                    <td className="border-y px-4 py-3 opacity-80">
                      {generacionDocumentalTypeLabel(row.documentType)}
                      {row.scenario ? ` · ${row.scenario}` : ""}
                    </td>
                    <td className="border-y px-4 py-3">
                      <StatusBadge label={vista.label} tone={vista.tone} />
                    </td>
                    <td className="rounded-r-xl border-y border-r px-4 py-3">
                      {errores.length > 0 ? (
                        <ul className="space-y-1">
                          {errores.map((error, index) => (
                            <li key={`${row.id}-${error.code ?? index}`} className="opacity-90">
                              {/* Codigo y campo: lo que fallo y donde. Nunca el valor capturado. */}
                              <span className="font-semibold">{error.code ?? "error"}</span>
                              {error.field ? <span className="opacity-70"> · {error.field}</span> : null}
                              {error.message ? <span className="block opacity-70">{error.message}</span> : null}
                            </li>
                          ))}
                        </ul>
                      ) : row.errorCode ? (
                        <span className="opacity-90">
                          <span className="font-semibold">{row.errorCode}</span>
                          {row.errorField ? <span className="opacity-70"> · {row.errorField}</span> : null}
                        </span>
                      ) : (
                        <span className="opacity-60">—</span>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>

        <Pagination
          page={page}
          pageSize={PAGE_SIZE}
          totalCount={totalCount}
          onPageChange={setPage}
        />
      </UiStateBoundary>
    </section>
  );
}
