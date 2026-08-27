'use client';

import { useEffect, useState } from 'react';
import { ocrResultForTipo, useProcedureDocuments } from '@/hooks/useProcedureDocuments';
import { tramitesClient } from '@/lib/api/tramites-client';
import { DocumentPreviewModal } from '@/components/shared/DocumentPreviewModal';
import { DocumentSlot } from './DocumentChecklist';
import { prendaDocLabelFor } from './prenda-document-tipos';
import type {
  ChecklistItemView,
  PrendaDecision,
  ProcedureAttachment,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

interface Props {
  instanceId: string | null;
  decision: PrendaDecision;
  docTipo: string;
  modalidad?: WizardModalidad;
  /** Compañía+OT: default true (obligatorio). false ⇒ badge Opcional. */
  documentRequired?: boolean;
  /** Notifica si el adjunto de soporte está presente (para el gate de Continuar). */
  onSatisfiedChange?: (satisfied: boolean) => void;
  onChanged?: () => void;
}

/**
 * Contenedor de carga del certificado de prenda (mismo diseño DocumentSlot).
 * Persiste vía attachments con tipo `prenda_solicitud` | `prenda_registro` | `prenda_levantamiento`.
 */
export function PrendaDocumentUpload({
  instanceId,
  decision,
  docTipo,
  modalidad = 'matricula_inicial',
  documentRequired = true,
  onSatisfiedChange,
  onChanged,
}: Props) {
  const { state, upload, remove } = useProcedureDocuments(instanceId, { modalidad });
  const { attachments, uploadingTipos, analyzingTipos, deletingId, ocrResults, error } = state;

  const attachment = attachments.find(
    (a) => a.tipo.toLowerCase() === docTipo.toLowerCase(),
  );

  useEffect(() => {
    onSatisfiedChange?.(!!attachment);
  }, [attachment, onSatisfiedChange]);

  const item: ChecklistItemView = {
    key: docTipo,
    label: prendaDocLabelFor(decision),
    obligatorio: documentRequired,
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
      // silencioso
    }
  };

  return (
    <div className="space-y-2" aria-label="Documento de soporte de prenda">
      <p className="text-xs font-semibold">Documento de soporte</p>
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
      <ul className="grid grid-cols-1 gap-3" aria-label="Carga del documento de prenda">
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
