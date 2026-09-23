'use client';

import { useCallback, useEffect, useState } from 'react';
import { Download, Eye, FileText } from 'lucide-react';
import { Modal } from '@/components/atom/Modal';
import { DocumentPreviewModal } from '@/components/shared/DocumentPreviewModal';
import { ICON_BUTTON_HIT_AREA } from '@/components/atom/RowActions';
import { tramitesClient } from '@/lib/api/tramites-client';
import { documentLabel } from '@/lib/tramites/document-labels';
import {
  COPY_DESCARGA_SIN_AUDITORIA,
  COPY_DOCUMENTOS_FUERA_DE_ALCANCE,
  isAuditUnavailable,
  isScopeRejection,
} from '@/lib/tramites/network-scope';
import { formatFechaHora } from '@/lib/format/date';
import type {
  ConsolidadoEntregaResult,
  ProcedureAttachment,
} from '@/lib/api/types/procedure-runtime';
import { esDocumentoDefinitivo } from '@/lib/tramites/consolidado-entrega';
import { AvisoDocumentoFinal } from '@/components/shared/AvisoDocumentoFinal';
import { AvisoFalloRegeneracion } from '@/components/shared/AvisoFalloRegeneracion';
import { detectarFalloRegeneracion } from '@/lib/tramites/fallo-regeneracion-consolidado';
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
export interface AttachmentPreviewOptions {
  /**
   * HU #12411 — trámite de un cliente hijo abierto por la cabeza de red: «Ver» y «Descargar» van
   * por las rutas proxeadas `network/**` (contrato B5 #12410). NUNCA se pide `preview-url`
   * (dirección prefirmada): el visor se alimenta del mismo binario de descarga re-empaquetado
   * como blob en memoria. Por defecto `false`: el trámite propio no cambia (AC4/AC5).
   */
  consultaMode?: boolean;
}

/** HU #12786 — mensaje cuando la ruta de entrega del consolidado falla (404/409/503/red). */
function mensajeEntregaFallida(e: unknown): string {
  return e instanceof Error && e.message
    ? `No se pudo obtener el consolidado vigente (${e.message}).`
    : 'No se pudo obtener el consolidado vigente.';
}

export function useAttachmentPreview(
  instanceId: string | null,
  tenantId?: string,
  options: AttachmentPreviewOptions = {},
) {
  const consultaMode = options.consultaMode === true;
  const [doc, setDoc] = useState<PreviewTarget | null>(null);
  const [url, setUrl] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  /**
   * HU #12411 — última acción rechazada por 503 `audit_unavailable` (auditoría fail-closed). Solo en
   * modo consulta. Mientras exista, la UI ofrece «Reintentar», que repite exactamente esa acción.
   */
  const [pendienteAuditoria, setPendienteAuditoria] = useState<{
    kind: 'open' | 'download';
    target: PreviewTarget;
  } | null>(null);
  /**
   * HU #12786 — última respuesta de la ruta de entrega del consolidado. Dice si el PDF servido es el
   * definitivo (AC4) y con qué `modo` se resolvió. `null` fuera del flujo del consolidado.
   */
  const [entrega, setEntrega] = useState<ConsolidadoEntregaResult | null>(null);

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
    setPendienteAuditoria(null);
    setEntrega(null);
  }, [revoke]);

  const open = useCallback(
    async (attachment: PreviewTarget) => {
      if (!instanceId) return;
      setDoc(attachment);
      revoke();
      setError(null);
      setPendienteAuditoria(null);
      setLoading(true);
      try {
        if (consultaMode) {
          // HU #12411 (AC2) — sin dirección prefirmada: el binario llega por la ruta de red y se
          // muestra desde un objectURL local. Un rechazo de alcance (403/404) no es fallo técnico.
          const { blob, mimetype } = await tramitesClient.downloadNetworkAttachment(
            instanceId,
            attachment.id,
            attachment.filename,
          );
          const type = attachment.mimetype || mimetype;
          setUrl(URL.createObjectURL(type ? new Blob([blob], { type }) : blob));
          return;
        }
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
        if (consultaMode && isAuditUnavailable(e)) {
          // 503 fail-closed: no hay binario, así que tampoco visor. Aviso reintentable en la sección.
          setDoc(null);
          setError(COPY_DESCARGA_SIN_AUDITORIA);
          setPendienteAuditoria({ kind: 'open', target: attachment });
          return;
        }
        if (consultaMode && isScopeRejection(e)) {
          setError(COPY_DOCUMENTOS_FUERA_DE_ALCANCE);
          return;
        }
        setError(
          e instanceof Error && e.message
            ? `No se pudo abrir el documento (${e.message}). Puedes descargarlo.`
            : 'No se pudo abrir el documento. Puedes descargarlo.',
        );
      } finally {
        setLoading(false);
      }
    },
    [instanceId, tenantId, revoke, consultaMode],
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
      setPendienteAuditoria(null);
      try {
        const { blob, filename } = consultaMode
          ? await tramitesClient.downloadNetworkAttachment(instanceId, target.id, target.filename)
          : await tramitesClient.downloadAttachment(instanceId, target.id, tenantId);
        const objectUrl = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = objectUrl;
        a.download = filename || target.filename;
        document.body.appendChild(a);
        a.click();
        a.remove();
        URL.revokeObjectURL(objectUrl);
      } catch (e: unknown) {
        if (consultaMode && isAuditUnavailable(e)) {
          setError(COPY_DESCARGA_SIN_AUDITORIA);
          setPendienteAuditoria({ kind: 'download', target });
          return;
        }
        if (consultaMode && isScopeRejection(e)) {
          setError(COPY_DOCUMENTOS_FUERA_DE_ALCANCE);
          return;
        }
        setError(e instanceof Error ? e.message : 'No se pudo descargar el documento.');
      }
    },
    [instanceId, doc, tenantId, consultaMode],
  );

  /**
   * HU #12786 — pide el consolidado VIGENTE a `GET …/consolidado/entrega` (una sola petición) y
   * devuelve el adjunto que hay que servir. El backend reconstruye solo si la bandera de vigencia
   * está abajo; si está arriba responde `modo: "vigente"` con el cacheado. El `attachmentId` del
   * resumen o de la lista de adjuntos NO se usa: puede ser el PDF antiguo.
   */
  const resolverConsolidado = useCallback(async (): Promise<PreviewTarget | null> => {
    if (!instanceId) return null;
    const res = await tramitesClient.entregarConsolidado(instanceId, {}, tenantId);
    setEntrega(res);
    return {
      id: res.document.attachmentId,
      tipo: res.document.tipo || 'consolidado',
      filename: res.document.filename,
      mimetype: 'application/pdf',
    };
  }, [instanceId, tenantId]);

  /**
   * HU #12786 (AC1/AC2/AC3) — abre el visor con el consolidado vigente. Mientras se resuelve la
   * entrega el visor ya está abierto en «cargando» con un marcador sin id; si la entrega falla, el
   * visor muestra el error (sin botón de descarga: no hay adjunto que bajar).
   */
  const openConsolidado = useCallback(
    async (filename = 'expediente-consolidado.pdf') => {
      if (!instanceId) return;
      revoke();
      setEntrega(null);
      setError(null);
      setPendienteAuditoria(null);
      setDoc({ id: '', tipo: 'consolidado', filename, mimetype: 'application/pdf' });
      setLoading(true);
      let target: PreviewTarget | null;
      try {
        target = await resolverConsolidado();
      } catch (e: unknown) {
        setError(mensajeEntregaFallida(e));
        setLoading(false);
        return;
      }
      if (target) await open(target);
    },
    [instanceId, revoke, resolverConsolidado, open],
  );

  /**
   * HU #12786 (AC1/AC3) — descarga directa del consolidado vigente: una petición a la entrega y una
   * a la descarga del adjunto que devolvió. No abre el visor.
   */
  const downloadConsolidado = useCallback(async () => {
    if (!instanceId) return;
    setError(null);
    setEntrega(null);
    let target: PreviewTarget | null;
    try {
      target = await resolverConsolidado();
    } catch (e: unknown) {
      setError(mensajeEntregaFallida(e));
      return;
    }
    if (target) await download(target);
  }, [instanceId, resolverConsolidado, download]);

  /**
   * HU #12411 — repite la acción rechazada por `audit_unavailable`. `null` cuando no hay nada que
   * reintentar (el 403/404 de alcance NO es reintentable: repetirlo no cambiaría nada).
   */
  const reintentar = pendienteAuditoria
    ? () => {
        const { kind, target } = pendienteAuditoria;
        setError(null);
        void (kind === 'open' ? open(target) : download(target));
      }
    : null;

  return {
    doc,
    url,
    loading,
    error,
    open,
    close,
    download,
    reintentar,
    entrega,
    /** HU #12786 (AC4) — el consolidado servido es el documento final del trámite. */
    definitivo: esDocumentoDefinitivo(entrega),
    /**
     * HU #12799 — la entrega sirvió el consolidado ANTERIOR porque regenerarlo falló. Esta vía no
     * conoce la vigencia del trámite, así que el aviso va sin fecha (no se inventa).
     */
    fallo: detectarFalloRegeneracion(entrega),
    openConsolidado,
    downloadConsolidado,
  };
}

/**
 * HU #12411 — aviso de una descarga/apertura fallida cuando el visor NO está abierto. Con
 * `reintentar` (503 `audit_unavailable`) añade el botón; con el 403/404 de alcance o un fallo técnico
 * solo el texto, como hasta ahora.
 */
export function AvisoDescargaFallida({
  preview,
  className,
}: {
  preview: Pick<ReturnType<typeof useAttachmentPreview>, 'doc' | 'error' | 'reintentar'>;
  className?: string;
}) {
  if (preview.doc !== null || !preview.error) return null;
  return (
    <div className={`flex flex-col items-start gap-2 ${className ?? ''}`} role="alert">
      <p className="text-xs" style={{ color: '#C2410C' }}>
        {preview.error}
      </p>
      {preview.reintentar ? (
        <button
          type="button"
          onClick={preview.reintentar}
          aria-label="Reintentar la descarga del documento"
          className="rounded-xl border px-3 py-1.5 text-xs font-semibold transition hover:bg-[#557EFF]/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
          style={{ borderColor: BLUE, color: BLUE }}
        >
          Reintentar
        </button>
      ) : null}
    </div>
  );
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
      // HU #12786 — el marcador del consolidado (sin id) aún no tiene adjunto que descargar.
      onDownload={preview.doc?.id ? () => void preview.download() : undefined}
      notice={
        preview.definitivo || preview.fallo ? (
          <div className="space-y-2">
            {preview.definitivo ? <AvisoDocumentoFinal /> : null}
            {preview.fallo ? <AvisoFalloRegeneracion fallo={preview.fallo} /> : null}
          </div>
        ) : undefined
      }
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

/** HU #12786 — el consolidado del trámite (no el maestro del OT) va por la ruta de entrega. */
function esConsolidado(d: Pick<ProcedureAttachment, 'tipo'>): boolean {
  return (d.tipo ?? '').toLowerCase() === 'consolidado';
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
                      {d.filename} · {formatFechaHora(d.uploadedAt)}
                    </p>
                  </div>
                  {/* Mismo par de botones de icono que el módulo de OT (`OtDocumentosTab`): ojo para
                      previsualizar en azul de marca, flecha para descargar en color de texto. */}
                  <button
                    type="button"
                    // HU #12786 (AC2) — el consolidado se pide a la ruta de entrega (vigente), no
                    // al adjunto persistido de la lista, que puede ser el antiguo.
                    onClick={() =>
                      void (esConsolidado(d) ? preview.openConsolidado(d.filename) : preview.open(d))
                    }
                    className={`${ICON_BUTTON_HIT_AREA} shrink-0 rounded-lg border border-border p-1.5 text-[#557EFF] transition hover:bg-[#557EFF]/10`}
                    aria-label={`Previsualizar ${documentLabel(d.tipo)}`}
                    title="Previsualizar"
                  >
                    <Eye className="h-4 w-4" aria-hidden="true" />
                  </button>
                  <button
                    type="button"
                    onClick={() =>
                      void (esConsolidado(d) ? preview.downloadConsolidado() : preview.download(d))
                    }
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
                  onClick={() => void preview.downloadConsolidado()}
                  aria-label="Descargar todo · Expediente consolidado (PDF)"
                >
                  <Download className="h-3.5 w-3.5" aria-hidden="true" />
                  Descargar todo · Expediente consolidado (PDF)
                </button>
              );
            })()}
          </>
        )}

        {/* HU #12786 (AC4) — descarga directa del definitivo: sin visor, el aviso va en el panel. */}
        {preview.doc === null && preview.definitivo && <AvisoDocumentoFinal className="mt-3" />}

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
