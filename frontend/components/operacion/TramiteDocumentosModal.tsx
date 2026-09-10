'use client';

import { useCallback, useEffect, useState } from 'react';
import { Download, Eye, FileText } from 'lucide-react';
import { Modal } from '@/components/atom/Modal';
import { DocumentPreviewModal } from '@/components/shared/DocumentPreviewModal';
import { ICON_BUTTON_HIT_AREA } from '@/components/atom/RowActions';
import { tramitesClient } from '@/lib/api/tramites-client';
import { documentLabel } from '@/lib/tramites/document-labels';
import { formatFecha } from '@/lib/format/date';
import type { ProcedureAttachment } from '@/lib/api/types/procedure-runtime';
import { findConsolidadoAttachment } from './ExpedienteVisor';

/**
 * HU #11054 / HU #11055 — consulta de los documentos de un trámite DESDE EL LISTADO, sin entrar al
 * wizard. Reutiliza el andamiaje que ya existía en el módulo de OT (ADR-0029): la URL prefirmada de
 * `preview-url` y el {@link DocumentPreviewModal} (PDF en iframe, imagen, y descarga como fallback
 * cuando el formato no se puede previsualizar).
 */

const BORDER = '#DFE5ED';
const BLUE = '#557EFF';

/**
 * Lo mínimo que el visor necesita de un adjunto. `ProcedureAttachment` encaja aquí tal cual, y el
 * listado puede abrir el consolidado con solo el id que trae el resumen (HU #11056), sin pedir
 * primero los adjuntos del trámite.
 */
export interface PreviewTarget {
  id: string;
  tipo: string;
  filename: string;
  mimetype: string;
}

/**
 * Previsualización de un adjunto de trámite. Vive en un hook porque lo usan dos entradas distintas
 * del listado: el panel de documentos (HU #11054) y la acción directa al expediente consolidado
 * (HU #11055), que abre el visor sin pasar por la lista.
 */
export function useAttachmentPreview(instanceId: string | null, tenantId?: string) {
  const [doc, setDoc] = useState<PreviewTarget | null>(null);
  const [url, setUrl] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  /** Libera el objectURL anterior: son blobs en memoria del navegador. */
  const revoke = useCallback(() => {
    setUrl((prev) => {
      if (prev) URL.revokeObjectURL(prev);
      return null;
    });
  }, []);

  const close = useCallback(() => {
    revoke();
    setDoc(null);
    setError(null);
    setLoading(false);
  }, [revoke]);

  const open = useCallback(
    async (attachment: PreviewTarget) => {
      if (!instanceId) return;
      setDoc(attachment);
      revoke();
      setError(null);
      setLoading(true);
      try {
        const res = await tramitesClient.fetchAttachmentPreviewUrl(
          instanceId,
          attachment.id,
          tenantId,
        );
        if (!res?.url) {
          setError('El servidor no devolvió una URL de previsualización.');
          return;
        }
        // El file-manager sirve el objeto como binary/octet-stream y SIN Content-Disposition, así que
        // un <iframe> apuntando a la URL prefirmada dispara la descarga en vez de mostrar el PDF. Se
        // re-empaquetan los bytes como Blob con el mimetype real para forzar el render inline; es la
        // misma técnica que ya usaba el módulo de OT (`OtDocumentosTab`).
        const raw = await fetch(res.url).then((r) => {
          if (!r.ok) throw new Error(String(r.status));
          return r.blob();
        });
        const typed = attachment.mimetype ? new Blob([raw], { type: attachment.mimetype }) : raw;
        setUrl(URL.createObjectURL(typed));
      } catch (e: unknown) {
        setError(
          e instanceof Error && e.message
            ? `No se pudo abrir el documento (${e.message}). Puedes descargarlo.`
            : 'No se pudo abrir el documento. Puedes descargarlo.',
        );
      } finally {
        setLoading(false);
      }
    },
    [instanceId, tenantId, revoke],
  );

  /**
   * Descarga un adjunto. Sirve para dos cosas: el fallback del visor cuando el formato no admite
   * previsualización (sin argumento: el documento abierto) y la descarga directa desde la lista
   * (con argumento: NO abre el visor).
   */
  const download = useCallback(
    async (attachment?: PreviewTarget) => {
      const target = attachment ?? doc;
      if (!instanceId || !target) return;
      try {
        const { blob, filename } = await tramitesClient.downloadAttachment(
          instanceId,
          target.id,
          tenantId,
        );
        const objectUrl = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = objectUrl;
        a.download = filename || target.filename;
        document.body.appendChild(a);
        a.click();
        a.remove();
        URL.revokeObjectURL(objectUrl);
      } catch (e: unknown) {
        setError(e instanceof Error ? e.message : 'No se pudo descargar el documento.');
      }
    },
    [instanceId, doc, tenantId],
  );

  return { doc, url, loading, error, open, close, download };
}

/** Visor del adjunto abierto por {@link useAttachmentPreview}. Nada si no hay documento abierto. */
export function AttachmentPreview({
  preview,
}: {
  preview: ReturnType<typeof useAttachmentPreview>;
}) {
  return (
    <DocumentPreviewModal
      open={preview.doc !== null}
      onClose={preview.close}
      title={preview.doc ? documentLabel(preview.doc.tipo) : 'Documento'}
      mimetype={preview.doc?.mimetype ?? null}
      url={preview.url}
      loading={preview.loading}
      error={preview.error}
      onDownload={() => void preview.download()}
    />
  );
}

export interface TramiteDocumentosModalProps {
  open: boolean;
  onClose: () => void;
  instanceId: string | null;
  /** Radicado, para titular el panel con el trámite que se está consultando. */
  referenceNumber: string;
  /** Tenant de la fila: el SuperAdmin consulta trámites de otras compañías. */
  tenantId?: string;
}

/** Panel de documentos del expediente de un trámite, abierto desde la fila del listado. */
export function TramiteDocumentosModal({
  open,
  onClose,
  instanceId,
  referenceNumber,
  tenantId,
}: TramiteDocumentosModalProps) {
  const [docs, setDocs] = useState<ProcedureAttachment[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const preview = useAttachmentPreview(instanceId, tenantId);

  useEffect(() => {
    if (!open || !instanceId) return;

    let cancelled = false;
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const list = await tramitesClient.getAttachments(instanceId, tenantId);
        if (!cancelled) setDocs(list);
      } catch (e: unknown) {
        if (!cancelled) {
          setError(e instanceof Error ? e.message : 'No se pudieron cargar los documentos.');
          setDocs([]);
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    };
    void load();

    return () => {
      cancelled = true;
    };
  }, [open, instanceId, tenantId]);

  return (
    <>
      <Modal
        open={open}
        onClose={onClose}
        title={`Documentos · ${referenceNumber}`}
        icon={FileText}
        size="xl"
      >
        {loading && (
          <div
            className="space-y-2 py-2"
            role="status"
            aria-busy="true"
            aria-label="Cargando documentos del trámite"
          >
            {[0, 1, 2].map((i) => (
              <div key={i} className="h-14 animate-pulse rounded-xl bg-[#F4F7FC] dark:bg-white/5" />
            ))}
          </div>
        )}

        {!loading && error && (
          <p className="py-4 text-sm" style={{ color: '#FF4E00' }} role="alert">
            {error}
          </p>
        )}

        {!loading && !error && docs.length === 0 && (
          <p className="py-6 text-center text-sm opacity-70">
            Este trámite aún no tiene documentos en el expediente.
          </p>
        )}

        {!loading && !error && docs.length > 0 && (
          <>
            <ul className="space-y-2" aria-label="Documentos del trámite">
              {docs.map((d) => (
                <li
                  key={d.id}
                  className="flex items-center gap-3 rounded-xl border p-3"
                  style={{ borderColor: BORDER }}
                >
                  <FileText className="h-4 w-4 shrink-0" style={{ color: BLUE }} aria-hidden="true" />
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-xs font-semibold text-[#162744] dark:text-white">
                      {documentLabel(d.tipo)}
                    </p>
                    <p className="truncate text-xs opacity-60">
                      {d.filename} · {formatFecha(d.uploadedAt)}
                    </p>
                  </div>
                  {/* Mismo par de botones de icono que el módulo de OT (`OtDocumentosTab`): ojo para
                      previsualizar en azul de marca, flecha para descargar en color de texto. */}
                  <button
                    type="button"
                    onClick={() => void preview.open(d)}
                    className={`${ICON_BUTTON_HIT_AREA} shrink-0 rounded-lg border border-border p-1.5 text-[#557EFF] transition hover:bg-[#557EFF]/10`}
                    aria-label={`Previsualizar ${documentLabel(d.tipo)}`}
                    title="Previsualizar"
                  >
                    <Eye className="h-4 w-4" aria-hidden="true" />
                  </button>
                  <button
                    type="button"
                    onClick={() => void preview.download(d)}
                    className={`${ICON_BUTTON_HIT_AREA} shrink-0 rounded-lg border border-border p-1.5 text-foreground transition hover:bg-[#557EFF]/10`}
                    aria-label={`Descargar ${documentLabel(d.tipo)}`}
                    title="Descargar"
                  >
                    <Download className="h-4 w-4" aria-hidden="true" />
                  </button>
                </li>
              ))}
            </ul>
            {(() => {
              const consolidado = findConsolidadoAttachment(docs);
              if (!consolidado) return null;
              return (
                <button
                  type="button"
                  className="mt-4 inline-flex items-center justify-center gap-2 rounded-full px-6 py-2.5 text-xs font-semibold text-white transition hover:opacity-95"
                  style={{
                    background: 'linear-gradient(135deg,#557EFF 0%,#00DBD5 100%)',
                    boxShadow: '0 10px 24px -6px rgba(85,126,255,0.45)',
                  }}
                  onClick={() => void preview.download(consolidado)}
                  aria-label="Descargar todo · Expediente consolidado (PDF)"
                >
                  <Download className="h-3.5 w-3.5" aria-hidden="true" />
                  Descargar todo · Expediente consolidado (PDF)
                </button>
              );
            })()}
          </>
        )}

        {/* Fallo de una descarga directa: el visor no está abierto, así que el aviso va aquí. */}
        {preview.doc === null && preview.error && (
          <p className="mt-3 text-xs" style={{ color: '#FF4E00' }} role="alert">
            {preview.error}
          </p>
        )}
      </Modal>

      <AttachmentPreview preview={preview} />
    </>
  );
}
