"use client";

/**
 * HU #10703 — Modal de previsualización de documentos (ADR-0029).
 * Soporta PDF (iframe), imagen (img) y fallback con botón de descarga.
 * 4 estados: idle (sin URL), cargando, cargado, error.
 */

import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { Download, FileText, X } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { openLoadingDocumentTab, openObjectUrlInWindow } from "@/lib/documents/open-document-tab";

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
  /**
   * HU #12786/#12787 — aviso sobre el documento mostrado (p. ej. «Documento final» cuando el
   * consolidado es el definitivo de un trámite en estado final). Se pinta sobre el cuerpo, en
   * cualquiera de los cuatro estados. Omitido = sin aviso (comportamiento previo).
   */
  notice?: ReactNode;
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

/** Cuánto vive la copia local del archivo que se le pasa a la pestaña (el visor del navegador ya la cargó). */
const VIDA_COPIA_PESTANA_MS = 120_000;

/**
 * Mejora (Bug #14604) — el documento se abre en una PESTAÑA NUEVA a pantalla completa, como el consolidado
 * del resumen del asistente, en vez de verse pequeño dentro del modal. Las props no cambian: quien lo usa
 * sigue pasando `open`/`loading`/`url`/`error` y la decisión se toma aquí.
 *
 * <ul>
 *   <li>La pestaña se abre en el mismo render que sigue al clic (sigue habiendo gesto del usuario, así el
 *   navegador no la bloquea) y muestra el carrito de «Cargando» mientras llega el archivo.</li>
 *   <li>El archivo se le pasa como copia local (blob): la URL firmada del almacenamiento nunca queda en la
 *   barra de direcciones ni en el historial, y no depende de que quien abrió el visor libere la suya.</li>
 *   <li>Error, archivo no previsualizable o sin archivo: la pestaña se cierra y el aviso (con «Descargar»)
 *   sale en la pantalla actual, en el modal de siempre.</li>
 *   <li>Si el navegador bloquea la pestaña, se usa el visor en modal como antes.</li>
 * </ul>
 */
export function DocumentPreviewModal(props: DocumentPreviewModalProps) {
  const { open, onClose, mimetype, url, loading, error, notice } = props;
  const pestana = useRef<Window | null>(null);
  /** La pestaña ya recibió el documento: al cerrar el visor NO se cierra. */
  const entregadoRef = useRef(false);
  /**
   * El aviso del documento («Documento final», «Maestro radicado»…) iba encima del PDF en el modal. Con la
   * pestaña se muestra en la pantalla actual; se guarda al entregar porque quien abrió el visor lo borra al
   * cerrarlo.
   */
  const noticeRef = useRef<ReactNode>(notice);
  useEffect(() => {
    noticeRef.current = notice;
  }, [notice]);
  const [avisoEnPantalla, setAvisoEnPantalla] = useState<ReactNode>(null);
  /** «pestana»: el documento va a otra pestaña; «modal»: el navegador la bloqueó y se usa el visor de siempre. */
  const [modo, setModo] = useState<"pestana" | "modal" | null>(null);
  const [entregado, setEntregado] = useState(false);
  const [falloEntrega, setFalloEntrega] = useState<string | null>(null);

  // Abre la pestaña apenas el visor se abre, todavía dentro del gesto del clic.
  useLayoutEffect(() => {
    if (!open) {
      if (pestana.current && !pestana.current.closed && !entregadoRef.current) pestana.current.close();
      pestana.current = null;
      entregadoRef.current = false;
      // eslint-disable-next-line react-hooks/set-state-in-effect -- al cerrar el visor se reinicia el vínculo con la pestaña (ventana externa).
      setModo(null);
      setEntregado(false);
      setFalloEntrega(null);
      return;
    }
    if (modo !== null) return;
    const win = typeof window.open === "function" ? openLoadingDocumentTab() : null;
    pestana.current = win;
    setModo(win ? "pestana" : "modal");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const resultadoEnPantalla =
    !loading && (Boolean(error) || !url || !isPreviewable(mimetype) || falloEntrega !== null);

  useEffect(() => {
    if (!open || modo !== "pestana" || loading || entregado) return;
    const win = pestana.current;
    if (resultadoEnPantalla) {
      if (win && !win.closed) win.close();
      return;
    }
    if (!url) return;
    let cancelado = false;
    void (async () => {
      try {
        const res = await fetch(url);
        if (!res.ok) throw new Error(String(res.status));
        const blob = await res.blob();
        if (cancelado) return;
        const copia = URL.createObjectURL(new Blob([blob], { type: mimetype ?? blob.type }));
        openObjectUrlInWindow(copia, win);
        window.setTimeout(() => URL.revokeObjectURL(copia), VIDA_COPIA_PESTANA_MS);
        entregadoRef.current = true;
        setEntregado(true);
        if (noticeRef.current) setAvisoEnPantalla(noticeRef.current);
        onClose();
      } catch {
        if (cancelado) return;
        if (win && !win.closed) win.close();
        setFalloEntrega("No se pudo abrir el documento en una pestaña nueva.");
      }
    })();
    return () => {
      cancelado = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, modo, loading, url, error, mimetype, entregado, resultadoEnPantalla]);

  // El aviso en pantalla se va solo; también se cierra con su botón.
  useEffect(() => {
    if (!avisoEnPantalla) return;
    const t = window.setTimeout(() => setAvisoEnPantalla(null), DURACION_AVISO_MS);
    return () => window.clearTimeout(t);
  }, [avisoEnPantalla]);

  const visible = open && (modo === "modal" || (modo === "pestana" && resultadoEnPantalla));
  return (
    <>
      <VisorEnModal {...props} open={visible} error={error ?? falloEntrega} />
      {avisoEnPantalla && typeof document !== "undefined"
        ? createPortal(
            <AvisoDelDocumento onCerrar={() => setAvisoEnPantalla(null)}>{avisoEnPantalla}</AvisoDelDocumento>,
            document.body,
          )
        : null}
    </>
  );
}

/** Cuánto se queda en pantalla el aviso del documento abierto en otra pestaña. */
const DURACION_AVISO_MS = 12_000;

/**
 * El aviso del documento (p. ej. «Documento final») en la pantalla actual, abajo a la derecha, cuando el
 * documento se abrió en otra pestaña. Por encima de los modales de detalle (z-1100/1150) y del visor (z-1200).
 */
function AvisoDelDocumento({ children, onCerrar }: { children: ReactNode; onCerrar: () => void }) {
  return (
    <div
      className="fixed bottom-4 right-4 z-[1300] w-[min(26rem,calc(100vw-2rem))] rounded-2xl border border-border bg-card p-3 shadow-lg"
      data-testid="aviso-documento-en-pantalla"
    >
      <div className="flex items-start gap-2">
        <div className="min-w-0 flex-1">{children}</div>
        <button
          type="button"
          onClick={onCerrar}
          className="shrink-0 rounded-lg p-1 opacity-70 hover:opacity-100"
          aria-label="Cerrar aviso"
        >
          <X className="h-4 w-4" aria-hidden="true" />
        </button>
      </div>
    </div>
  );
}

/** El visor dentro del modal: respaldo cuando no hay pestaña y lugar de los avisos de error o sin vista previa. */
function VisorEnModal({
  open,
  onClose,
  title,
  mimetype,
  url,
  loading,
  error,
  onDownload,
  notice,
}: DocumentPreviewModalProps) {
  return (
    <Modal
      open={open}
      onClose={onClose}
      title={title}
      icon={FileText}
      size="lg"
      // La previsualización SIEMPRE va encima de lo que la abrió, y hoy la abren superficies que
      // ya están muy arriba en la pila: el shell del detalle de trámite (z-1100) y el modal del
      // asistente (z-1150). Con el z-110 anterior el visor se montaba DETRÁS de ellas y el usuario
      // pulsaba el ojo sin que pasara nada visible.
      zClassName="z-[1200]"
    >
      <div
        className="min-h-[50vh] flex flex-col"
        data-testid="preview-modal-body"
      >
        {notice ? <div className="mb-3">{notice}</div> : null}
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
                style={{ background: "var(--color-flit-brand)" }}
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
                style={{ background: "var(--color-flit-brand)" }}
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
                    style={{ background: "var(--color-flit-brand)" }}
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
                  className="flex items-center gap-1.5 rounded-xl border border-[#DFE5ED] dark:border-white/15 px-3 py-1.5 text-[11px] font-semibold text-flit-primary dark:text-slate-100"
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
