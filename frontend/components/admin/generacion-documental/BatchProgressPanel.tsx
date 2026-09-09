"use client";

import { useCallback, useState } from "react";
import { Download } from "lucide-react";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import { downloadStandaloneBatchZip } from "@/lib/api/admin-generacion-documental";
import { ApiError } from "@/lib/api/types";
import { BatchItemsTable } from "./BatchItemsTable";
import { standaloneBatchStatusView } from "./status-labels";
import { useBatchPolling } from "./useBatchPolling";

export interface BatchProgressPanelProps {
  batchId: string;
}

/**
 * Seguimiento in-app de un lote XLSX (HU #12211; CF-13, CF-14, CF-15, CF-21, CF-22).
 *
 * <p><b>Progreso anunciado con `aria-live`</b> (CF-14): la región de estado es `polite`, así que
 * un lector de pantalla escucha «12 de 100 procesados» sin que el foco salte. El resultado no se
 * comunica solo por color (CF-22): la etiqueta del estado es texto y los conteos son números
 * visibles.</p>
 *
 * <p><b>`queued` y `processing` se muestran ambos como «En proceso»</b>, y `partial_failure` como
 * un resultado con su conteo de generados y de errores. Las etiquetas NO se escriben aquí: salen
 * de `status-labels.ts`, el único archivo del módulo que las declara (CF-21).</p>
 *
 * <p><b>La descarga ZIP no promete lo que no puede dar:</b> un lote sin ningún documento generado
 * responde 409 y aquí se muestra la explicación del backend, en vez de entregar un archivo vacío.</p>
 */
export function BatchProgressPanel({ batchId }: BatchProgressPanelProps) {
  const { batch, status, polling, refresh } = useBatchPolling(batchId);
  const [descargando, setDescargando] = useState(false);
  const [errorDescarga, setErrorDescarga] = useState<string | null>(null);

  const descargarZip = useCallback(async () => {
    setErrorDescarga(null);
    setDescargando(true);
    try {
      // La descarga se fuerza en el cliente: el storage solo firma URLs inline.
      await downloadStandaloneBatchZip(batchId);
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) {
        setErrorDescarga(
          error.message || "El lote no tiene documentos generados para descargar.",
        );
      } else if (error instanceof ApiError && error.status === 404) {
        setErrorDescarga("No encontramos ese lote en tu compañía.");
      } else {
        setErrorDescarga("No se pudo descargar el ZIP del lote. Intenta nuevamente.");
      }
    } finally {
      setDescargando(false);
    }
  }, [batchId]);

  if (status === "notFound") {
    return (
      <section className="rounded-2xl border p-6" aria-labelledby="lote-no-encontrado">
        <h2 id="lote-no-encontrado" className="text-sm font-semibold">
          No encontramos ese lote
        </h2>
        <p className="mt-2 text-xs opacity-80">
          El lote no existe o no pertenece a tu compañía.
        </p>
      </section>
    );
  }

  const vista = batch ? standaloneBatchStatusView(batch.status) : null;
  const total = batch?.total ?? 0;
  const procesados = batch?.processed ?? 0;
  const porcentaje = total > 0 ? Math.min(100, Math.round((procesados / total) * 100)) : 0;

  return (
    <div className="flex flex-1 flex-col gap-4">
      <UiStateBoundary
        status={status === "ready" ? "ready" : status === "error" ? "error" : "loading"}
        onRetry={refresh}
        skeletonRows={3}
        errorMessage="No se pudo consultar el avance del lote. Intenta nuevamente."
      >
        {batch && vista && (
          <section aria-labelledby="lote-avance-titulo" className="rounded-2xl border p-4">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <h2 id="lote-avance-titulo" className="text-xs font-semibold uppercase opacity-70">
                Avance del lote
              </h2>
              <StatusBadge label={vista.label} tone={vista.tone} />
            </div>

            {/* CF-14 — el progreso se ANUNCIA: la region es aria-live polite y su texto es la
                frase completa, no un numero suelto que un lector leeria sin contexto. */}
            <p
              className="mt-3 text-sm"
              role="status"
              aria-live="polite"
              aria-atomic="true"
              data-testid="lote-progreso"
            >
              {`${procesados} de ${total} filas procesadas · ${batch.generated} generadas · ${batch.errors} con error`}
            </p>

            <div
              className="mt-3 h-2 w-full overflow-hidden rounded-full"
              style={{ background: "#DFE5ED" }}
              role="progressbar"
              aria-valuenow={porcentaje}
              aria-valuemin={0}
              aria-valuemax={100}
              aria-label="Progreso del lote"
            >
              <div
                className="h-full rounded-full transition-all"
                style={{ width: `${porcentaje}%`, background: "#557EFF" }}
              />
            </div>

            <p className="mt-2 text-[11px] opacity-70">
              {batch.isTerminal
                ? "El lote terminó. Esta vista ya no se actualiza sola."
                : polling
                  ? "Actualizando automáticamente cada 4 segundos."
                  : "Actualización pausada mientras la pestaña está en segundo plano."}
            </p>

            <div className="mt-4 flex flex-wrap items-center gap-2">
              <button
                type="button"
                onClick={() => void descargarZip()}
                disabled={descargando}
                aria-busy={descargando}
                className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
                style={{ background: "#557EFF" }}
              >
                <Download className="h-3.5 w-3.5" aria-hidden="true" />
                {descargando ? "Preparando ZIP…" : "Descargar documentos generados"}
              </button>

              {!batch.isTerminal && (
                <button
                  type="button"
                  onClick={refresh}
                  className="rounded-xl border px-3 py-2 text-[11px] font-semibold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
                >
                  Actualizar ahora
                </button>
              )}
            </div>

            {errorDescarga && (
              <p
                role="alert"
                className="mt-3 rounded-xl border px-4 py-2 text-xs"
                style={{ borderColor: "#FF4E00" }}
              >
                {errorDescarga}
              </p>
            )}
          </section>
        )}
      </UiStateBoundary>

      <BatchItemsTable batchId={batchId} refreshKey={batch?.processed ?? 0} />
    </div>
  );
}
