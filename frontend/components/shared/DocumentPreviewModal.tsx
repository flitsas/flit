"use client";

/**
 * HU #10703 — Modal de previsualización de documentos (ADR-0029).
 * Soporta PDF (iframe), imagen (img) y fallback con botón de descarga.
 * 4 estados: idle (sin URL), cargando, cargado, error.
 */

import { Download, FileText } from "lucide-react";
import { Modal } from "@/components/atom/Modal";

export type PreviewState = "idle" | "loading" | "loaded" | "error";

export interface DocumentPreviewModalProps {
  open: boolean;
  onClose: () => void;
  title: string;
  /** MIME type del adjunto (necesario para decidir qué renderizar). */
  mimetype: string | null;
  /** URL presignada GET con Content-Disposition inline. null → estado idle. */
  url: string | null;
  /** El contenedor está resolviendo la URL → mostrar skeleton. */
  loading: boolean;
  /** Mensaje de error al obtener la URL. */
  error: string | null;
  /** Fallback: dispara la descarga cuando no se puede previsualizar. */
  onDownload?: () => void;
}

function isPreviewable(mime: string | null): boolean {
  if (!mime) return false;
  return (
    mime === "application/pdf" ||
    mime === "image/jpeg" ||
    mime === "image/png" ||
    mime === "image/webp"
  );
}

function isImage(mime: string | null): boolean {
  return (
    mime === "image/jpeg" || mime === "image/png" || mime === "image/webp"
  );
}

export function DocumentPreviewModal({
  open,
  onClose,
  title,
  mimetype,
  url,
  loading,
  error,
  onDownload,
}: DocumentPreviewModalProps) {
  return (
    <Modal
      open={open}
      onClose={onClose}
      title={title}
      icon={FileText}
      size="lg"
      zClassName="z-[110]"
    >
      <div
        className="min-h-[50vh] flex flex-col"
        data-testid="preview-modal-body"
      >
        {/* Estado: cargando */}
        {loading && (
          <div
            className="flex-1 flex items-center justify-center min-h-[50vh]"
            role="status"
            aria-busy="true"
            aria-label="Cargando previsualización"
            data-testid="preview-loading"
          >
            <div className="space-y-3 w-full px-4">
              {[...Array(4)].map((_, i) => (
                <div
                  key={i}
                  className="h-10 rounded-xl animate-pulse bg-[#F4F7FC] dark:bg-white/5"
                />
              ))}
            </div>
          </div>
        )}

        {/* Estado: error */}
        {!loading && error && (
          <div
            className="flex-1 flex flex-col items-center justify-center gap-4 py-12"
            role="alert"
            data-testid="preview-error"
          >
            <p className="text-sm text-center opacity-80">{error}</p>
            {onDownload && (
              <button
                type="button"
                className="flex items-center gap-2 rounded-xl px-4 py-2 text-xs font-semibold text-white"
                style={{ background: "#557EFF" }}
                aria-label="Descargar documento"
                onClick={onDownload}
              >
                <Download className="h-3.5 w-3.5" aria-hidden="true" />
                Descargar
              </button>
            )}
          </div>
        )}

        {/* Estado: idle (sin URL, sin error, sin carga) */}
        {!loading && !error && !url && (
          <div
            className="flex-1 flex flex-col items-center justify-center gap-4 py-12"
            data-testid="preview-idle"
          >
            <FileText
              className="h-12 w-12 opacity-30"
              aria-hidden="true"
            />
            <p className="text-sm opacity-60 text-center">
              No hay previsualización disponible.
            </p>
            {onDownload && (
              <button
                type="button"
                className="flex items-center gap-2 rounded-xl px-4 py-2 text-xs font-semibold text-white"
                style={{ background: "#557EFF" }}
                aria-label="Descargar documento"
                onClick={onDownload}
              >
                <Download className="h-3.5 w-3.5" aria-hidden="true" />
                Descargar
              </button>
            )}
          </div>
        )}

        {/* Estado: cargado */}
        {!loading && !error && url && (
          <>
            {/* PDF */}
            {mimetype === "application/pdf" && (
              <iframe
                src={url}
                title={title}
                className="w-full flex-1 min-h-[60vh] rounded-xl border border-[#DFE5ED] dark:border-white/10"
                data-testid="preview-iframe"
              />
            )}

            {/* Imagen */}
            {isImage(mimetype) && (
              <div className="flex-1 flex items-center justify-center p-2">
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img
                  src={url}
                  alt={title}
                  className="max-w-full max-h-[70vh] rounded-xl border border-[#DFE5ED] dark:border-white/10 object-contain"
                  data-testid="preview-image"
                />
              </div>
            )}

            {/* Fallback para tipos no previsables */}
            {!isPreviewable(mimetype) && (
              <div
                className="flex-1 flex flex-col items-center justify-center gap-4 py-12"
                data-testid="preview-fallback"
              >
                <FileText
                  className="h-12 w-12 opacity-30"
                  aria-hidden="true"
                />
                <p className="text-sm opacity-60 text-center">
                  Este tipo de archivo no se puede previsualizar.
                </p>
                {onDownload && (
                  <button
                    type="button"
                    className="flex items-center gap-2 rounded-xl px-4 py-2 text-xs font-semibold text-white"
                    style={{ background: "#557EFF" }}
                    aria-label="Descargar documento"
                    onClick={onDownload}
                  >
                    <Download className="h-3.5 w-3.5" aria-hidden="true" />
                    Descargar
                  </button>
                )}
              </div>
            )}

            {/* Botón de descarga siempre disponible en la parte inferior */}
            {isPreviewable(mimetype) && onDownload && (
              <div className="mt-3 flex justify-end">
                <button
                  type="button"
                  className="flex items-center gap-1.5 rounded-xl border border-[#DFE5ED] dark:border-white/15 px-3 py-1.5 text-[11px] font-semibold text-[#162744] dark:text-slate-100"
                  aria-label="Descargar documento"
                  onClick={onDownload}
                >
                  <Download className="h-3.5 w-3.5" aria-hidden="true" />
                  Descargar
                </button>
              </div>
            )}
          </>
        )}
      </div>
    </Modal>
  );
}
