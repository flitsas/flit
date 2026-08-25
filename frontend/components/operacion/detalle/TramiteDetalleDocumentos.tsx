'use client';

import { useEffect, useState } from 'react';
import { Download } from 'lucide-react';
import {
  AttachmentPreview,
  useAttachmentPreview,
} from '@/components/operacion/TramiteDocumentosModal';
import { tramitesClient } from '@/lib/api/tramites-client';
import { documentLabel } from '@/lib/tramites/document-labels';
import { formatFecha } from '@/lib/format/date';
import type {
  ChecklistItemView,
  ProcedureAttachment,
} from '@/lib/api/types/procedure-runtime';
import {
  SeccionCargando,
  SeccionError,
  SeccionVacia,
  TarjetaDetalle,
  DetalleBadgeSoft,
  type SeccionDetalleProps,
} from './primitivos';
import { DETALLE_BLUE, DETALLE_GREEN, DETALLE_GOLD } from './detalle-visual';

/**
 * Sección «Documentos» del modal de detalle (Frente C, `Paso3` de la propuesta).
 *
 * Dos listas del contrato, DELIBERADAMENTE separadas (ver nota del encargo):
 * - `getChecklist` → qué se le EXIGE al trámite; `satisfied` es la única fuente de si un
 *   requisito está cumplido. NUNCA se deriva de contar adjuntos.
 * - `getAttachments` → qué hay CARGADO. Se empareja con el checklist por `docTipo` ↔ `tipo`
 *   solo para ofrecer el botón de descarga; el estado del requisito no depende de este emparejamiento.
 *
 * La propuesta pinta un botón «Descargar paquete»: no existe una llamada que devuelva el paquete
 * completo del expediente (`downloadAttachment`/`getAttachments` solo dan adjuntos sueltos), así que
 * se omite — ver respuesta del encargo.
 */

const AZUL = DETALLE_BLUE;

function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) return '—';
  if (bytes < 1024) return `${bytes} B`;
  const units = ['KB', 'MB', 'GB'];
  let value = bytes / 1024;
  let unitIndex = 0;
  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024;
    unitIndex += 1;
  }
  return `${value.toFixed(value < 10 ? 1 : 0)} ${units[unitIndex]}`;
}

/** Botón de descarga común a las dos rejillas: mismo estilo, foco visible, nombre accesible. */
function DownloadButton({
  filename,
  onClick,
}: {
  filename: string;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label={`Descargar ${filename}`}
      title="Descargar"
      className="shrink-0 rounded-lg border p-1.5 transition hover:bg-[#557EFF]/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 border-[#DFE5ED] dark:border-white/10"
      style={{ color: AZUL }}
    >
      <Download className="h-3.5 w-3.5" aria-hidden="true" />
    </button>
  );
}

/** Fila de un requisito del checklist: etiqueta (+ Opcional), badge tintado por `satisfied`, descarga. */
function RequisitoRow({
  item,
  attachment,
  onDownload,
}: {
  item: ChecklistItemView;
  attachment: ProcedureAttachment | undefined;
  onDownload: (attachment: ProcedureAttachment) => void;
}) {
  return (
    <li
      className="flex items-center justify-between gap-2 rounded-xl border px-3 py-2 border-[#DFE5ED] dark:border-white/10"
    >
      <span className="min-w-0">
        <span className="block truncate text-xs font-medium text-[#162744] dark:text-white">
          {item.label}
        </span>
        {!item.obligatorio ? (
          <span className="block text-xs text-[#162744]/70 dark:text-white/70">Opcional</span>
        ) : null}
      </span>
      <span className="flex shrink-0 items-center gap-2">
        <DetalleBadgeSoft
          text={item.satisfied ? '✓ Adjunto' : 'Sin adjuntar'}
          color={item.satisfied ? DETALLE_GREEN : DETALLE_GOLD}
        />
        {attachment ? (
          <DownloadButton filename={attachment.filename} onClick={() => onDownload(attachment)} />
        ) : null}
      </span>
    </li>
  );
}

/** Fila de un adjunto que no casa con ningún requisito del checklist («Otros adjuntos»). */
function AdjuntoRow({
  attachment,
  onDownload,
}: {
  attachment: ProcedureAttachment;
  onDownload: (attachment: ProcedureAttachment) => void;
}) {
  return (
    <li
      className="flex items-center justify-between gap-2 rounded-xl border px-3 py-2 border-[#DFE5ED] dark:border-white/10"
    >
      <span className="min-w-0">
        <span className="block truncate text-xs font-medium text-[#162744] dark:text-white">
          {documentLabel(attachment.tipo)} · {attachment.filename}
        </span>
        <span className="block text-xs text-[#162744]/70 dark:text-white/70">
          {formatBytes(attachment.sizeBytes)} · {formatFecha(attachment.uploadedAt)}
        </span>
      </span>
      <DownloadButton filename={attachment.filename} onClick={() => onDownload(attachment)} />
    </li>
  );
}

export function TramiteDetalleDocumentos({ instanceId, tenantId }: SeccionDetalleProps) {
  const [checklist, setChecklist] = useState<ChecklistItemView[]>([]);
  const [attachments, setAttachments] = useState<ProcedureAttachment[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  const preview = useAttachmentPreview(instanceId, tenantId);

  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const [checklistRes, attachmentsRes] = await Promise.all([
          tramitesClient.getChecklist(instanceId, tenantId),
          tramitesClient.getAttachments(instanceId, tenantId),
        ]);
        if (!cancelled) {
          setChecklist(checklistRes?.items ?? []);
          setAttachments(attachmentsRes ?? []);
        }
      } catch (e: unknown) {
        if (!cancelled) {
          setError(e instanceof Error ? e.message : 'No se pudieron cargar los documentos del trámite.');
          setChecklist([]);
          setAttachments([]);
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    };
    void load();
    return () => {
      cancelled = true;
    };
  }, [instanceId, tenantId, reloadKey]);

  const checklistDocTipos = new Set(checklist.map((i) => i.docTipo).filter(Boolean));
  const otrosAdjuntos = attachments.filter(
    (a) => a.source !== 'system' && !checklistDocTipos.has(a.tipo),
  );
  const isEmpty = checklist.length === 0 && otrosAdjuntos.length === 0;

  if (loading) {
    return (
      <TarjetaDetalle titulo="Documentos del trámite">
        <SeccionCargando etiqueta="Cargando documentos del trámite" />
      </TarjetaDetalle>
    );
  }

  if (error) {
    return (
      <TarjetaDetalle titulo="Documentos del trámite">
        <SeccionError mensaje={error} onReintentar={() => setReloadKey((k) => k + 1)} />
      </TarjetaDetalle>
    );
  }

  if (isEmpty) {
    return (
      <TarjetaDetalle titulo="Documentos del trámite">
        <SeccionVacia mensaje="Este trámite no tiene requisitos ni documentos registrados." />
      </TarjetaDetalle>
    );
  }

  return (
    <div className="flex flex-col gap-4">
      <TarjetaDetalle titulo="Documentos del trámite" className="h-full">
        {checklist.length === 0 ? (
          <SeccionVacia mensaje="Este trámite no tiene requisitos de documentos configurados." />
        ) : (
          <ul className="grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3" aria-label="Requisitos del trámite">
            {checklist.map((item) => {
              const attachment = item.docTipo
                ? attachments.find((a) => a.tipo === item.docTipo)
                : undefined;
              return (
                <RequisitoRow
                  key={item.key}
                  item={item}
                  attachment={attachment}
                  onDownload={(a) => void preview.download(a)}
                />
              );
            })}
          </ul>
        )}
      </TarjetaDetalle>

      {otrosAdjuntos.length > 0 ? (
        <TarjetaDetalle titulo="Otros adjuntos">
          <ul className="flex flex-col gap-2" aria-label="Otros adjuntos del trámite">
            {otrosAdjuntos.map((a) => (
              <AdjuntoRow key={a.id} attachment={a} onDownload={(att) => void preview.download(att)} />
            ))}
          </ul>
        </TarjetaDetalle>
      ) : null}

      {/* Fallo de una descarga directa: el visor no llega a abrirse, así que el aviso va aquí (mismo
          patrón que TramiteDetalleModal / TramiteDocumentosModal). */}
      {preview.doc === null && preview.error ? (
        <p className="text-xs" style={{ color: '#C2410C' }} role="alert">
          {preview.error}
        </p>
      ) : null}

      <AttachmentPreview preview={preview} />
    </div>
  );
}
