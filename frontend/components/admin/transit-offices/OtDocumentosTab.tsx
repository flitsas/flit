"use client";

/**
 * HU #10705 — Tab de documentos del expediente de un trámite de cliente OT.
 * Tabla: filename, tipo, tamaño, fecha, acciones (previsualizar + descargar).
 * Botón de consolidado (estándar o maestro).
 */

import { useCallback, useEffect, useState } from "react";
import { Download, Eye, FileText } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { DocumentPreviewModal } from "@/components/shared/DocumentPreviewModal";
import {
  descargarOtConsolidado,
  fetchOtAttachmentPreviewUrl,
  fetchOtDocuments,
  generarOtConsolidado,
  generarOtConsolidadoMaestro,
} from "@/lib/api/admin-ot";
import { downloadFile } from "@/lib/api/download";
import type { OtApiScope, OtProcedureAttachment } from "@/lib/api/admin-ot";

export interface OtDocumentosTabProps {
  /** ID del trámite de cliente OT. */
  procedureId: string;
  /** Número de radicado (para el nombre del consolidado descargado). */
  referenceNumber?: string;
  scope?: OtApiScope;
  /** Si es true el OT no puede generar/ver consolidado (solo ver documentos). */
  readOnly?: boolean;
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function formatDate(iso: string): string {
  try {
    return new Intl.DateTimeFormat("es-CO", {
      day: "2-digit",
      month: "2-digit",
      year: "numeric",
      hour: "2-digit",
      minute: "2-digit",
    }).format(new Date(iso));
  } catch {
    return iso;
  }
}

export function OtDocumentosTab({
  procedureId,
  referenceNumber,
  scope,
  readOnly = false,
}: OtDocumentosTabProps) {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [attachments, setAttachments] = useState<OtProcedureAttachment[]>([]);
  const [consolidadoActing, setConsolidadoActing] = useState(false);

  // Preview modal state
  const [previewItem, setPreviewItem] = useState<OtProcedureAttachment | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [previewLoading, setPreviewLoading] = useState(false);
  const [previewError, setPreviewError] = useState<string | null>(null);

  const load = useCallback(async (signal?: AbortSignal) => {
    setStatus("loading");
    try {
      const result = await fetchOtDocuments(procedureId, scope);
      if (signal?.aborted) return;
      const list = result.data ?? [];
      setAttachments(list);
      setStatus(list.length === 0 ? "empty" : "ready");
    } catch {
      if (!signal?.aborted) setStatus("error");
    }
  }, [procedureId, scope]);

  useEffect(() => {
    const c = new AbortController();
    void load(c.signal);
    return () => c.abort();
  }, [load]);

  const handlePreview = async (item: OtProcedureAttachment) => {
    setPreviewItem(item);
    setPreviewUrl(null);
    setPreviewError(null);
    setPreviewLoading(true);
    try {
      const result = await fetchOtAttachmentPreviewUrl(procedureId, item.id, scope);
      setPreviewUrl(result.url);
    } catch {
      setPreviewError("No se pudo obtener la URL de previsualización. Descarga el archivo.");
    } finally {
      setPreviewLoading(false);
    }
  };

  const handleDownload = async (item: OtProcedureAttachment) => {
    try {
      await downloadFile(
        `/api/v1/admin/ot/client-procedures/${procedureId}/attachments/${item.id}/download`,
        {
          query: scope?.transitOfficeId ? { transitOfficeId: scope.transitOfficeId } : undefined,
          fallbackFilename: item.filename,
        },
      );
    } catch {
      show("No se pudo descargar el archivo.", "error");
    }
  };

  const handleConsolidado = async (maestro = false) => {
    setConsolidadoActing(true);
    try {
      if (maestro) {
        await generarOtConsolidadoMaestro(procedureId, scope);
        show("Consolidado maestro generado.", "success");
      } else {
        await generarOtConsolidado(procedureId, scope);
        show("Consolidado generado.", "success");
      }
      void load();
    } catch {
      show("No se pudo generar el consolidado.", "error");
    } finally {
      setConsolidadoActing(false);
    }
  };

  const handleVerConsolidado = async () => {
    setConsolidadoActing(true);
    try {
      await descargarOtConsolidado(procedureId, referenceNumber, scope);
    } catch {
      show("El trámite aún no tiene consolidado generado.", "error");
    } finally {
      setConsolidadoActing(false);
    }
  };

  return (
    <>
      <DocumentPreviewModal
        open={!!previewItem}
        onClose={() => {
          setPreviewItem(null);
          setPreviewUrl(null);
          setPreviewError(null);
        }}
        title={previewItem?.filename ?? "Previsualización"}
        mimetype={previewItem?.mimetype ?? null}
        url={previewUrl}
        loading={previewLoading}
        error={previewError}
        onDownload={previewItem ? () => void handleDownload(previewItem) : undefined}
      />

      <div className="space-y-4" data-testid="ot-documentos-tab">
        {/* Barra de acciones */}
        {!readOnly && (
          <div className="flex flex-wrap items-center justify-end gap-2">
            <button
              type="button"
              className="rounded-xl border px-3 py-1.5 text-[11px] font-semibold disabled:opacity-50"
              style={{ borderColor: "#DFE5ED", color: "#162744" }}
              disabled={consolidadoActing}
              aria-label="Generar consolidado estándar"
              onClick={() => void handleConsolidado(false)}
            >
              {consolidadoActing ? "Generando…" : "Generar consolidado"}
            </button>
            <button
              type="button"
              className="rounded-xl border px-3 py-1.5 text-[11px] font-semibold disabled:opacity-50"
              style={{ borderColor: "#DFE5ED", color: "#162744" }}
              disabled={consolidadoActing}
              aria-label="Generar consolidado maestro"
              onClick={() => void handleConsolidado(true)}
            >
              {consolidadoActing ? "Generando…" : "Generar consolidado maestro"}
            </button>
            <button
              type="button"
              className="rounded-xl border px-3 py-1.5 text-[11px] font-semibold disabled:opacity-50"
              style={{ borderColor: "#DFE5ED", color: "#162744" }}
              disabled={consolidadoActing}
              aria-label="Ver consolidado"
              onClick={() => void handleVerConsolidado()}
            >
              Ver consolidado
            </button>
          </div>
        )}

        <UiStateBoundary
          status={status}
          emptyMessage="Este trámite no tiene documentos adjuntos."
          errorMessage="Error al cargar los documentos del trámite."
          onRetry={() => void load()}
          skeletonRows={4}
        >
          <table
            className="w-full border-separate border-spacing-y-2 text-xs"
            aria-label="Documentos del expediente"
          >
            <thead>
              <tr
                className="text-left text-[10px] font-semibold uppercase"
                style={{ color: "#162744" }}
              >
                <th className="rounded-l-xl px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                  Archivo
                </th>
                <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                  Tipo
                </th>
                <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                  Tamaño
                </th>
                <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                  Fecha
                </th>
                <th
                  className="rounded-r-xl px-4 py-2.5 text-right"
                  style={{ background: "#DFE5ED" }}
                >
                  Acciones
                </th>
              </tr>
            </thead>
            <tbody>
              {attachments.map((att) => (
                <tr key={att.id} className="bg-white dark:bg-[#0B0F14]">
                  <td className="rounded-l-xl border-y border-l px-4 py-3">
                    <div className="flex items-center gap-2">
                      <FileText
                        className="h-4 w-4 shrink-0 opacity-40"
                        aria-hidden="true"
                      />
                      <span className="truncate font-medium max-w-[200px]" title={att.filename}>
                        {att.filename}
                      </span>
                    </div>
                  </td>
                  <td className="border-y px-4 py-3 opacity-70">{att.tipo}</td>
                  <td className="border-y px-4 py-3 opacity-70">
                    {formatSize(att.sizeBytes)}
                  </td>
                  <td className="border-y px-4 py-3 opacity-70">
                    {formatDate(att.uploadedAt)}
                  </td>
                  <td className="rounded-r-xl border-y border-r px-4 py-3">
                    <div className="flex items-center justify-end gap-2">
                      <button
                        type="button"
                        className="rounded-lg border p-1.5"
                        style={{ borderColor: "#DFE5ED", color: "#557EFF" }}
                        aria-label={`Previsualizar ${att.filename}`}
                        onClick={() => void handlePreview(att)}
                      >
                        <Eye className="h-3.5 w-3.5" aria-hidden="true" />
                      </button>
                      <button
                        type="button"
                        className="rounded-lg border p-1.5"
                        style={{ borderColor: "#DFE5ED", color: "#162744" }}
                        aria-label={`Descargar ${att.filename}`}
                        onClick={() => void handleDownload(att)}
                      >
                        <Download className="h-3.5 w-3.5" aria-hidden="true" />
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </UiStateBoundary>
      </div>
    </>
  );
}
