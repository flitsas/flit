'use client';

import { useEffect, useState } from 'react';
import { ocrResultForTipo, useProcedureDocuments } from '@/hooks/useProcedureDocuments';
import { tramitesClient } from '@/lib/api/tramites-client';
import { DocumentPreviewModal } from '@/components/shared/DocumentPreviewModal';
import { DocumentSlot } from './DocumentChecklist';
import type {
  CamaraComercioRequirement,
  ChecklistItemView,
  ProcedureAttachment,
} from '@/lib/api/types/procedure-runtime';

/**
 * HU #12774 — tipo de adjunto del certificado, por rol. Debe coincidir con el catálogo
 * (`118-HU12774-camara-comercio-por-rol.sql`) y con `CamaraComercioAttachmentTipo` del backend.
 *
 * <p>No se reutiliza `camara_comercio` a secas: ese código es del catálogo de paridad FLIT 1.0 y
 * arrastra los datos migrados de V1, donde las tres llaves del legado colapsan en él.</p>
 */
export function camaraComercioTipo(rol: string): string {
  return `camara_comercio_${rol.trim().toLowerCase()}`;
}

/** Texto que explica por qué el buzón quedó opcional. Null cuando es obligatorio. */
export function textoExencion(exencion: CamaraComercioRequirement['exencion']): string | null {
  if (exencion === 'firma_y_escritura') {
    return 'Este actor cuenta con firma precargada y escritura vigentes, así que el certificado de Cámara de Comercio es opcional: no necesitas cargarlo para continuar con el trámite.';
  }
  return null;
}

interface Props {
  instanceId: string | null;
  /** Requisito resuelto por el backend para ESTA parte. */
  requirement: CamaraComercioRequirement;
  /** Notifica si el requisito quedó satisfecho (alimenta el gate de «Continuar» del paso). */
  onSatisfiedChange?: (satisfied: boolean) => void;
  onChanged?: () => void;
}

/**
 * Carga del certificado de Cámara de Comercio del actor persona jurídica.
 *
 * <p>Va dentro del paso del actor, pegado a sus datos, y no en Requisitos: el certificado acredita a
 * la sociedad que se acaba de capturar, y mandarlo dos pasos más allá obligaría al gestor a recordar
 * por qué se lo piden. Mismo criterio que <c>EscrituraRepresentanteUpload</c>, que además es su
 * pariente documental: certificado y escritura acreditan lo mismo —quién representa a la sociedad— y
 * son sustitutos entre sí.</p>
 *
 * <p><b>El buzón nunca se oculta</b> mientras el actor sea persona jurídica. Lo único que cambia es
 * si bloquea: solo con firma precargada Y escritura vigentes aparece como opcional.</p>
 */
export function CamaraComercioUpload({
  instanceId,
  requirement,
  onSatisfiedChange,
  onChanged,
}: Props) {
  const docTipo = requirement.tipo || camaraComercioTipo(requirement.rol);
  const { state, upload, remove } = useProcedureDocuments(instanceId);
  const { attachments, uploadingTipos, analyzingTipos, deletingId, ocrResults, error } = state;

  const attachment = attachments.find((a) => a.tipo.toLowerCase() === docTipo.toLowerCase());

  // Opcional ⇒ el paso puede avanzar aunque no haya nada cargado. Obligatorio ⇒ hace falta el
  // adjunto. Se calcula aquí y no en el padre para que el gate y lo que ve el gestor salgan del
  // mismo sitio.
  const satisfied = !requirement.esObligatorio || !!attachment;

  useEffect(() => {
    onSatisfiedChange?.(satisfied);
  }, [satisfied, onSatisfiedChange]);

  const exencion = textoExencion(requirement.exencion);
  const alertaVigencia = !!attachment && requirement.vigencia === 'excedida';

  const item: ChecklistItemView = {
    key: docTipo,
    label: 'Certificado de Cámara de Comercio',
    obligatorio: requirement.esObligatorio,
    docTipo,
    satisfied: !!attachment,
    // El criterio funcional es PDF y solo PDF; el catálogo ya lo impone en el backend y aquí se
    // declara para que el selector de archivos no ofrezca lo que se va a rechazar.
    mimeTypesAllowed: ['application/pdf'],
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

  // Tintado del contenedor: ámbar cuando falta algo obligatorio, neutro cuando es opcional. Los
  // valores salen de las variables de globals.css para que el tema oscuro funcione solo.
  const pendiente = requirement.esObligatorio && !attachment;

  return (
    <div
      className="space-y-2 rounded-xl border p-3"
      style={
        pendiente
          ? {
              borderColor: 'var(--badge-warning-border)',
              background: 'var(--badge-warning-bg)',
            }
          : { borderColor: 'var(--border-soft, #DFE5ED)' }
      }
      aria-label="Certificado de Cámara de Comercio"
    >
      {/* El estado obligatorio/opcional NO se repite aquí: lo pinta `DocumentSlot`, que es su dueño
          canónico («Por cargar» / «Opcional» / «Cargado»). Duplicarlo en el contenedor obligaba al
          gestor a leer dos veces lo mismo, y con dos vocabularios distintos. */}
      <p
        className="text-xs font-semibold"
        style={{ color: pendiente ? 'var(--badge-warning-fg)' : undefined }}
      >
        Certificado de Cámara de Comercio
      </p>

      <p className="text-xs opacity-80">
        {exencion ??
          'Adjunta el certificado de existencia y representación legal expedido por una Cámara de Comercio para poder continuar.'}
      </p>

      {/* AlertCard de vigencia: informativa, nunca bloquea. Solo aparece cuando hay documento Y el
          OCR pudo leer la fecha — `indeterminada` no pinta nada, porque una fecha ilegible no es un
          documento vencido. */}
      {alertaVigencia && (
        <p
          className="rounded-lg border px-2 py-1.5 text-xs"
          role="status"
          style={{
            background: 'var(--badge-warning-bg)',
            color: 'var(--badge-warning-fg)',
            borderColor: 'var(--badge-warning-border)',
          }}
        >
          Este certificado tiene{' '}
          {requirement.diasDesdeExpedicion !== null
            ? `${requirement.diasDesdeExpedicion} días`
            : 'más de 30 días'}{' '}
          de expedición. Te recomendamos actualizarlo antes de radicar el trámite. Puedes continuar
          con el documento cargado.
        </p>
      )}

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

      <ul className="grid grid-cols-1 gap-3" aria-label="Carga del certificado de Cámara de Comercio">
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
