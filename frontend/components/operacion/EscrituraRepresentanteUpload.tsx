'use client';

import { useEffect, useState } from 'react';
import { ocrResultForTipo, useProcedureDocuments } from '@/hooks/useProcedureDocuments';
import { tramitesClient } from '@/lib/api/tramites-client';
import { DocumentPreviewModal } from '@/components/shared/DocumentPreviewModal';
import { DocumentSlot } from './DocumentChecklist';
import type {
  ActorRol,
  ChecklistItemView,
  ProcedureAttachment,
} from '@/lib/api/types/procedure-runtime';

/**
 * Tipo de adjunto de la escritura del representante legal, por rol de la parte.
 *
 * <p>Un código por rol —misma convención que <c>certificado_identidad{_rol}</c>— para que las dos
 * partes jurídicas de un traspaso puedan cargar cada una la suya sin pisarse: el emparejamiento
 * adjunto ↔ requisito es por tipo, así que un solo código dejaría a la segunda parte "satisfecha"
 * con el documento de la primera.</p>
 *
 * <p>NO se reutiliza <c>escritura</c> / <c>escritura_comprador</c>: esos son documentos de SISTEMA
 * (los resuelve el directorio de la compañía) y la limpieza de huérfanos del expediente los retira
 * sin mirar el origen, así que una carga manual bajo ese código no sobreviviría a la siguiente
 * regeneración del expediente.</p>
 */
export function escrituraRepresentanteTipo(rol: ActorRol): string {
  return rol === 'comprador' ? 'escritura_representante' : `escritura_representante_${rol}`;
}

interface Props {
  instanceId: string | null;
  /** Rol de la parte jurídica cuyo representante hay que acreditar. */
  rol: ActorRol;
  /** Notifica si el adjunto está presente (alimenta el gate de "Continuar" del paso). */
  onSatisfiedChange?: (satisfied: boolean) => void;
  onChanged?: () => void;
}

/**
 * Carga de la escritura (o poder) que acredita al representante legal capturado en el trámite.
 *
 * <p>Se pinta dentro del bloque «Representante legal y/o apoderado», y solo cuando ese representante
 * NO está en el módulo de representantes de la compañía. Cuando sí lo está, su escritura vive en el
 * directorio y el sistema la apalanca sola: pedir una carga ahí sería trabajo duplicado.</p>
 *
 * <p>Va aquí y no en Requisitos a propósito: el documento acredita a la persona que se acaba de
 * capturar, y separarlos obligaría al gestor a recordar, dos pasos más allá, por qué le piden una
 * escritura.</p>
 */
export function EscrituraRepresentanteUpload({
  instanceId,
  rol,
  onSatisfiedChange,
  onChanged,
}: Props) {
  const docTipo = escrituraRepresentanteTipo(rol);
  const { state, upload, remove } = useProcedureDocuments(instanceId);
  const { attachments, uploadingTipos, analyzingTipos, deletingId, ocrResults, error } = state;

  const attachment = attachments.find((a) => a.tipo.toLowerCase() === docTipo.toLowerCase());

  useEffect(() => {
    onSatisfiedChange?.(!!attachment);
  }, [attachment, onSatisfiedChange]);

  const item: ChecklistItemView = {
    key: docTipo,
    label: 'Escritura o poder del representante legal',
    obligatorio: true,
    docTipo,
    satisfied: !!attachment,
  };

  const [previewAttachment, setPreviewAttachment] = useState<ProcedureAttachment | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [previewLoading, setPreviewLoading] = useState(false);
  const [previewError, setPreviewError] = useState<string | null>(null);

  const handlePreview = async (att: ProcedureAttachment) => {
    if (!instanceId) return;
    setPreviewAttachment(att);
    setPreviewUrl((prev) => {
      if (prev) URL.revokeObjectURL(prev);
      return null;
    });
    setPreviewError(null);
    setPreviewLoading(true);
    try {
      const result = await tramitesClient.fetchAttachmentPreviewUrl(instanceId, att.id);
      const blob = await fetch(result.url).then((r) => {
        if (!r.ok) throw new Error(String(r.status));
        return r.blob();
      });
      const typed = att.mimetype ? new Blob([blob], { type: att.mimetype }) : blob;
      setPreviewUrl(URL.createObjectURL(typed));
    } catch {
      setPreviewError(
        'No se pudo obtener la URL de previsualización. Descarga el archivo en su lugar.',
      );
    } finally {
      setPreviewLoading(false);
    }
  };

  const closePreview = () => {
    setPreviewUrl((prev) => {
      if (prev) URL.revokeObjectURL(prev);
      return null;
    });
    setPreviewAttachment(null);
    setPreviewError(null);
  };

  const handleDownloadFromPreview = async () => {
    if (!instanceId || !previewAttachment) return;
    try {
      const { blob, filename, mimetype } = await tramitesClient.downloadAttachment(
        instanceId,
        previewAttachment.id,
        undefined,
        previewAttachment.filename,
      );
      const objectUrl = URL.createObjectURL(new Blob([blob], { type: mimetype }));
      const a = document.createElement('a');
      a.href = objectUrl;
      a.download = filename;
      a.click();
      URL.revokeObjectURL(objectUrl);
    } catch {
      // silencioso: la descarga es una comodidad del modal, no el camino principal.
    }
  };

  return (
    <div
      className="space-y-2 rounded-xl border p-3"
      style={{ borderColor: '#F9AC00', background: 'rgba(249,172,0,0.06)' }}
      aria-label="Escritura del representante legal"
    >
      {/* Copy Flit 2.0 · S2 — alerta de representante no autorizado. */}
      <p className="text-xs font-semibold" style={{ color: '#B45309' }}>
        Nuevo representante legal detectado
      </p>
      <p className="text-xs opacity-80">
        El representante legal o apoderado ingresado aún no cuenta con autorización en el sistema.
        Para continuar, por favor adjunta la Escritura Pública correspondiente que certifique su
        nombramiento.
      </p>
      {error && (
        <p className="text-xs" style={{ color: '#FF4E00' }} role="alert">
          {error}
        </p>
      )}
      <DocumentPreviewModal
        open={!!previewAttachment}
        onClose={closePreview}
        title={previewAttachment?.filename ?? 'Previsualización'}
        mimetype={previewAttachment?.mimetype ?? null}
        url={previewUrl}
        loading={previewLoading}
        error={previewError}
        onDownload={previewAttachment ? () => void handleDownloadFromPreview() : undefined}
      />
      <ul className="grid grid-cols-1 gap-3" aria-label="Carga de la escritura del representante legal">
        <DocumentSlot
          item={item}
          attachment={attachment}
          uploading={uploadingTipos.has(docTipo)}
          analyzing={analyzingTipos.has(docTipo)}
          deleting={!!attachment && deletingId === attachment.id}
          ocr={ocrResultForTipo(ocrResults, docTipo)}
          onUpload={(file) =>
            void upload(docTipo, file).then((ok) => {
              if (ok) onChanged?.();
            })
          }
          onRemove={(id) =>
            void remove(id).then((ok) => {
              if (ok) onChanged?.();
            })
          }
          onPreview={instanceId ? (att) => void handlePreview(att) : undefined}
        />
      </ul>
    </div>
  );
}
