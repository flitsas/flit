"use client";

/**
 * HU #10705 — Tab de documentos del expediente de un trámite de cliente OT.
 * Tabla: filename, tipo, tamaño, fecha, acciones (previsualizar + descargar).
 * Consolidado: "Generar consolidado" (expediente completo del trámite) + "Ver consolidado"
 * (previsualización inline, sin descarga).
 */

import { useCallback, useEffect, useState } from "react";
import { Download, Eye, FileText } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { DocumentPreviewModal } from "@/components/shared/DocumentPreviewModal";
import {
  fetchOtAttachmentPreviewUrl,
  fetchOtDocuments,
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
    setPreviewUrl((prev) => {
      if (prev) URL.revokeObjectURL(prev);
      return null;
    });
    setPreviewError(null);
    setPreviewLoading(true);
    try {
      const result = await fetchOtAttachmentPreviewUrl(procedureId, item.id, scope);
      // El file-manager sirve el objeto como binary/octet-stream sin Content-Disposition, por lo que
      // un <iframe> con la URL directa fuerza descarga. Re-empaquetamos los bytes como Blob con el
      // mimetype real para forzar el render inline en el navegador (S3 permite CORS GET).
      const blob = await fetch(result.url).then((r) => {
        if (!r.ok) throw new Error(String(r.status));
        return r.blob();
      });
      const typed = item.mimetype ? new Blob([blob], { type: item.mimetype }) : blob;
      setPreviewUrl(URL.createObjectURL(typed));
    } catch {
      setPreviewError("No se pudo obtener la URL de previsualización. Descarga el archivo.");
    } finally {
      setPreviewLoading(false);
    }
  };

  const closePreview = () => {
    setPreviewUrl((prev) => {
      if (prev) URL.revokeObjectURL(prev);
      return null;
    });
    setPreviewItem(null);
    setPreviewError(null);
  };

  const handleDownload = async (item: OtProcedureAttachment) => {
    try {
      await downloadFile(
        `/api/v1/admin/ot/client-procedures/${procedureId}/documents/${item.id}/download`,
        {
          query: scope?.transitOfficeId ? { transitOfficeId: scope.transitOfficeId } : undefined,
          fallbackFilename: item.filename,
        },
      );
    } catch {
      show("No se pudo descargar el archivo.", "error");
    }
  };

  const handleGenerarConsolidado = async () => {
    setConsolidadoActing(true);
    try {
      // El consolidado del OT es el expediente completo: incluye el 100% de los documentos
      // cargados y generados del trámite (endpoint "maestro"), ordenados por la matriz documental.
      await generarOtConsolidadoMaestro(procedureId, scope);
      show("Consolidado generado.", "success");
      void load();
    } catch {
      show("No se pudo generar el consolidado.", "error");
    } finally {
      setConsolidadoActing(false);
    }
  };

  const handleVerConsolidado = () => {
    // Previsualiza el consolidado INLINE (sin forzar descarga), reusando el modal de preview.
    const consolidado =
      attachments.find((a) => a.tipo === "consolidado_maestro") ??
      attachments.find((a) => a.tipo === "consolidado");
    if (!consolidado) {
      show("Primero genera el consolidado.", "error");
      return;
    }
    void handlePreview(consolidado);
  };

  return (
    <>
      <DocumentPreviewModal
        open={!!previewItem}
        onClose={closePreview}
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
              className="rounded-xl border border-border px-3 py-1.5 text-[11px] font-semibold text-foreground disabled:opacity-50"
              disabled={consolidadoActing}
              aria-label="Generar consolidado del expediente completo"
              title="Genera el expediente consolidado con todos los documentos del trámite"
              onClick={() => void handleGenerarConsolidado()}
            >
              {consolidadoActing ? "Generando…" : "Generar consolidado"}
            </button>
            <button
              type="button"
              className="rounded-xl border border-border px-3 py-1.5 text-[11px] font-semibold text-foreground disabled:opacity-50"
              disabled={consolidadoActing}
              aria-label="Ver consolidado"
              title="Previsualiza el consolidado sin descargarlo"
              onClick={() => handleVerConsolidado()}
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
          <div className="overflow-x-auto">
          <table
            className="w-full min-w-[640px] border-separate border-spacing-y-2 text-xs"
            aria-label="Documentos del expediente"
          >
            <thead>
              <tr className="text-left text-[10px] font-semibold uppercase text-foreground">
                <th className="rounded-l-xl px-4 py-2.5 bg-muted">
                  Archivo
                </th>
                <th className="px-4 py-2.5 bg-muted">
                  Tipo
                </th>
                <th className="px-4 py-2.5 bg-muted">
                  Tamaño
                </th>
                <th className="px-4 py-2.5 bg-muted">
                  Fecha
                </th>
                <th className="rounded-r-xl px-4 py-2.5 text-right bg-muted">
                  Acciones
                </th>
              </tr>
            </thead>
            <tbody>
              {attachments.map((att) => (
                <tr key={att.id} className="bg-card">
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
                        className="rounded-lg border border-border p-1.5 text-[#557EFF]"
                        aria-label={`Previsualizar ${att.filename}`}
                        title="Previsualizar"
                        onClick={() => void handlePreview(att)}
                      >
                        <Eye className="h-3.5 w-3.5" aria-hidden="true" />
                      </button>
                      <button
                        type="button"
                        className="rounded-lg border border-border p-1.5 text-foreground"
                        aria-label={`Descargar ${att.filename}`}
                        title="Descargar"
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
          </div>
        </UiStateBoundary>
      </div>
    </>
  );
}
