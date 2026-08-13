'use client';

import { useState, type ReactNode } from 'react';
import { Download, FileText } from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import {
  openLoadingDocumentTab,
  openObjectUrlInWindow,
  showDocumentTabError,
} from '@/lib/documents/open-document-tab';
import { documentLabel } from '@/lib/tramites/document-labels';
import { WizardAccordion } from './WizardAccordion';
import { WizardCardHeader } from './wizard-atoms';
import type {
  InstanceStatus,
  ProcedureAttachment,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

// Expediente digital: documentos del trámite. Vehículo, actores y validación
// viven en MatriculaResumen. El organismo de tránsito no se muestra aquí
// (se elige en el paso 1 o lo fija el RUNT en traspaso).

interface Props {
  instanceId: string | null;
  attachments: ProcedureAttachment[];
  modalidad?: WizardModalidad;
  status?: InstanceStatus;
  onBeforeGenerateConsolidado?: () => Promise<void>;
  onAttachmentsChange?: () => void;
}

const BLUE = '#557EFF';
const BORDER = '#DFE5ED';

/** Solo el consolidado del wizard (`tipo === 'consolidado'`). Nunca el maestro ni fuzzy match. */
export function findConsolidadoAttachment(
  attachments: ProcedureAttachment[],
): ProcedureAttachment | undefined {
  return attachments.find((a) => (a.tipo ?? '').toLowerCase() === 'consolidado');
}

function ExpedienteDisclosure({
  title,
  defaultOpen = true,
  children,
}: {
  title: string;
  defaultOpen?: boolean;
  children: ReactNode;
}) {
  const [open, setOpen] = useState(defaultOpen);

  return (
    <WizardAccordion
      title={title}
      open={open}
      onOpenChange={setOpen}
      icon={<span className="h-4 w-1 shrink-0 rounded-full" style={{ background: BLUE }} aria-hidden="true" />}
    >
      {children}
    </WizardAccordion>
  );
}

/**
 * Abre el adjunto en pestaña nueva lo más rápido posible:
 * 1) abre la pestaña al instante (gesto del usuario) con el carrito rodando,
 * 2) pide URL prefirmada (sin proxy del binario por core-api),
 * 3) baja desde storage y re-empaqueta el Blob con el MIME real (PDF inline).
 * Fallback a /download si falla preview-url.
 */
export async function openAttachmentInNewTab(
  instanceId: string,
  attachment: Pick<ProcedureAttachment, 'id' | 'tipo' | 'filename' | 'mimetype'>,
) {
  const win = openLoadingDocumentTab();
  const mime =
    attachment.mimetype?.trim() ||
    (attachment.tipo === 'consolidado' || (attachment.filename ?? '').toLowerCase().endsWith('.pdf')
      ? 'application/pdf'
      : 'application/octet-stream');

  try {
    let blob: Blob;
    try {
      const preview = await tramitesClient.fetchAttachmentPreviewUrl(
        instanceId,
        attachment.id,
      );
      if (!preview?.url) throw new Error('preview_url_empty');
      const raw = await fetch(preview.url).then((r) => {
        if (!r.ok) throw new Error(`storage_${r.status}`);
        return r.blob();
      });
      blob = new Blob([raw], { type: mime });
    } catch {
      const downloaded = await tramitesClient.downloadAttachment(
        instanceId,
        attachment.id,
        undefined,
        attachment.filename,
      );
      blob = downloaded.mimetype
        ? new Blob([downloaded.blob], { type: downloaded.mimetype })
        : new Blob([downloaded.blob], { type: mime });
    }

    const objectUrl = URL.createObjectURL(blob);
    openObjectUrlInWindow(objectUrl, win);
    window.setTimeout(() => URL.revokeObjectURL(objectUrl), 120_000);
  } catch (err) {
    showDocumentTabError(win);
    throw err;
  }
}

export default function ExpedienteVisor({
  instanceId,
  attachments,
  modalidad = 'matricula_inicial',
  status = 'borrador',
  onBeforeGenerateConsolidado,
  onAttachmentsChange,
}: Props) {
  return (
    <section aria-label="Expediente digital" className="space-y-3">
      <DocumentosSection
        instanceId={instanceId}
        attachments={attachments}
        modalidad={modalidad}
        status={status}
        onBeforeGenerateConsolidado={onBeforeGenerateConsolidado}
        onAttachmentsChange={onAttachmentsChange}
      />
    </section>
  );
}

function consolidadoAvisoLabel(aviso: string): string {
  const [documento, motivo = ''] = aviso.split(':').map((s) => s.trim());
  const nombre =
    documento === 'documentos_del_expediente'
      ? 'algunos documentos del expediente'
      : documentLabel(documento);
  const causa =
    motivo.includes('organismo_requerido')
      ? ' (falta el organismo de tránsito)'
      : motivo.includes('provider_unavailable')
        ? ' (el proveedor no está disponible; vuelve a generar el expediente en unos minutos)'
        : motivo.includes('provider_validation')
          ? ' (el proveedor rechazó los datos del trámite)'
          : motivo
            ? ` (${motivo})`
            : '';
  return `${nombre}${causa}`;
}

function DocumentosSection({
  instanceId,
  attachments,
  modalidad,
  status,
  onBeforeGenerateConsolidado,
  onAttachmentsChange,
}: {
  instanceId: string | null;
  attachments: ProcedureAttachment[];
  modalidad: WizardModalidad;
  status: InstanceStatus;
  onBeforeGenerateConsolidado?: () => Promise<void>;
  onAttachmentsChange?: () => void;
}) {
  const consolidado = findConsolidadoAttachment(attachments);
  const [generating, setGenerating] = useState(false);
  const [downloading, setDownloading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const estadoFinal = status === 'aprobado' || status === 'anulado';
  const busy = generating || downloading;

  const applyAvisos = (generado: Awaited<ReturnType<typeof tramitesClient.generarConsolidado>>) => {
    const avisos: string[] = [];
    if (generado?.incompleto) {
      const faltantes = (generado.documentosFaltantes ?? []).map(documentLabel).join(', ');
      avisos.push(
        faltantes
          ? `Faltan documentos obligatorios: ${faltantes}.`
          : 'Faltan documentos obligatorios.',
      );
    }
    for (const aviso of generado?.avisosCascada ?? []) {
      avisos.push(`No se pudo generar ${consolidadoAvisoLabel(aviso)}.`);
    }
    if (avisos.length > 0) {
      setError(`Expediente consolidado generado. ${avisos.join(' ')}`);
    }
  };

  const consolidadoIdFromResult = (
    generado: Awaited<ReturnType<typeof tramitesClient.generarConsolidado>>,
  ): { id: string; filename: string } | null => {
    const nested = generado?.document;
    if (nested?.attachmentId) {
      return { id: nested.attachmentId, filename: nested.filename || 'consolidado.pdf' };
    }
    // Compat con respuestas planas (mocks / clientes antiguos).
    const flat = generado as { attachmentId?: string; filename?: string } | null | undefined;
    if (flat?.attachmentId) {
      return { id: flat.attachmentId, filename: flat.filename || 'consolidado.pdf' };
    }
    return null;
  };

  const handleGenerate = async () => {
    if (!instanceId) return;
    setGenerating(true);
    setError(null);
    try {
      await onBeforeGenerateConsolidado?.();
      // force=true: invalida caché y reconstruye sin anidar un consolidado previo (evita docs duplicados).
      const generado = await tramitesClient.generarConsolidado(instanceId, undefined, true);
      applyAvisos(generado);
      onAttachmentsChange?.();
    } catch (err) {
      const msg = (err instanceof Error ? err.message : '').trim();
      setError(
        msg.includes('generacion_bloqueada_estado_final')
          ? 'El trámite ya está aprobado o anulado: su documentación es definitiva y no se regenera.'
          : msg ||
              'No se pudo generar el consolidado. Revisa la conexión e inténtalo de nuevo.',
      );
    } finally {
      setGenerating(false);
    }
  };

  /**
   * Siempre regenera con force=true y abre el PDF nuevo.
   * Abrir un consolidado cacheado podía mostrar todos los documentos duplicados si se había
   * generado anidando un consolidado_maestro u otro paquete previo.
   */
  const handleDescargarTodo = async () => {
    if (!instanceId) return;
    setDownloading(true);
    setError(null);
    try {
      await onBeforeGenerateConsolidado?.();
      const generado = await tramitesClient.generarConsolidado(instanceId, undefined, true);
      applyAvisos(generado);
      const doc = consolidadoIdFromResult(generado);
      if (doc) {
        await openAttachmentInNewTab(instanceId, {
          id: doc.id,
          tipo: 'consolidado',
          filename: doc.filename,
          mimetype: 'application/pdf',
        });
      }
      onAttachmentsChange?.();
    } catch (err) {
      const msg = (err instanceof Error ? err.message : '').trim();
      setError(
        msg.includes('generacion_bloqueada_estado_final')
          ? 'El trámite ya está aprobado o anulado: su documentación es definitiva y no se regenera.'
          : msg ||
              'No se pudo generar el consolidado. Revisa la conexión e inténtalo de nuevo.',
      );
    } finally {
      setDownloading(false);
    }
  };

  const gradientBtnClass =
    'inline-flex items-center justify-center rounded-full px-6 py-2.5 text-xs font-semibold text-white transition hover:opacity-95 disabled:opacity-50';
  const gradientBtnStyle = { background: 'linear-gradient(90deg,#557EFF 0%,#00DBD5 100%)' };

  return (
    <ExpedienteDisclosure title="Documentos">
      {attachments.length > 0 ? (
        <ul className="space-y-2.5" aria-label="Documentos del expediente (visor)">
          {attachments.map((a) => (
            <DocRow key={a.id} instanceId={instanceId} attachment={a} />
          ))}
        </ul>
      ) : (
        <p className="text-xs opacity-60">No se han cargado documentos.</p>
      )}

      <div className="mt-4 space-y-3 border-t pt-4" style={{ borderColor: BORDER }}>
        <WizardCardHeader
          title="Expediente consolidado"
          level="h4"
          className=""
          subtitle={`Un solo PDF con el FUR, el certificado de identidad, la impronta y los documentos cargados en el trámite${modalidad === 'traspaso' ? ' (incluye el contrato de compraventa)' : ''}. Al generarlo se producen también los documentos que falten.`}
        />

        {error && (
          <div
            className="rounded-xl border p-3 text-xs"
            style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
            role="alert"
            aria-live="polite"
          >
            {error}
          </div>
        )}

        {estadoFinal ? (
          <p className="text-xs font-medium" style={{ color: '#557EFF' }} role="status">
            El trámite ya está {status === 'aprobado' ? 'aprobado' : 'anulado'}: su documentación es
            definitiva. Puedes consultarla y descargarla.
          </p>
        ) : null}

        <div className="flex flex-wrap items-center gap-2">
          {!estadoFinal && consolidado ? (
            <button
              type="button"
              onClick={() => void handleGenerate()}
              disabled={busy || !instanceId}
              className="rounded-full px-5 py-2.5 text-xs font-semibold text-white disabled:opacity-50"
              style={{ background: '#162744' }}
            >
              {generating ? 'Generando expediente…' : 'Re-generar expediente consolidado'}
            </button>
          ) : null}

          {instanceId && (!estadoFinal || consolidado) ? (
            <button
              type="button"
              className={gradientBtnClass}
              style={gradientBtnStyle}
              disabled={busy}
              onClick={() => void handleDescargarTodo()}
              aria-label="Ver expediente consolidado (PDF)"
            >
              {downloading
                ? 'Generando expediente…'
                : 'Ver expediente consolidado (PDF)'}
            </button>
          ) : null}
        </div>
      </div>
    </ExpedienteDisclosure>
  );
}

function DocRow({
  instanceId,
  attachment: d,
}: {
  instanceId: string | null;
  attachment: ProcedureAttachment;
}) {
  const [busy, setBusy] = useState(false);
  const label = documentLabel(d.tipo) || d.filename || d.tipo || 'Documento';
  const filename = d.filename?.trim() || '';

  return (
    <li
      className="flex items-center gap-3 rounded-2xl border bg-white px-4 py-3 dark:bg-[#162744]"
      style={{ borderColor: BORDER }}
    >
      <FileText className="h-5 w-5 shrink-0" style={{ color: BLUE }} aria-hidden="true" />
      <div className="min-w-0 flex-1">
        <p className="truncate text-xs">
          <span className="font-semibold" style={{ color: '#162744' }}>
            {label}
          </span>
          {filename ? (
            <span className="font-normal opacity-55"> · {filename}</span>
          ) : null}
        </p>
        {d.sha256 ? (
          <p className="mt-0.5 truncate font-mono text-xs opacity-45" title={d.sha256}>
            SHA-256 {d.sha256}
          </p>
        ) : null}
      </div>
      <button
        type="button"
        disabled={!instanceId || busy}
        className="inline-flex shrink-0 items-center gap-1.5 rounded-full px-4 py-2 text-xs font-semibold text-white disabled:opacity-50"
        style={{ background: BLUE }}
        aria-label={`Ver ${filename || label}`}
        onClick={async () => {
          if (!instanceId) return;
          setBusy(true);
          try {
            await openAttachmentInNewTab(instanceId, d);
          } finally {
            setBusy(false);
          }
        }}
      >
        <Download className="h-3.5 w-3.5" aria-hidden="true" />
        {busy ? 'Abriendo…' : 'Ver'}
      </button>
    </li>
  );
}
