'use client';

import { useEffect, useState } from 'react';
import { Download, Eye } from 'lucide-react';
import {
  AttachmentPreview,
  AvisoDescargaFallida,
  useAttachmentPreview,
} from '@/components/operacion/TramiteDocumentosModal';
import { tramitesClient } from '@/lib/api/tramites-client';
import { useConsultaMode } from '@/components/operacion/ConsultaModeContext';
import {
  COPY_DOCUMENTOS_FUERA_DE_ALCANCE,
  ETIQUETA_SOLO_CONSULTA,
  describirErrorDeSeccion,
} from '@/lib/tramites/network-scope';
import { documentLabel } from '@/lib/tramites/document-labels';
import { DocumentCatalogCaption } from '@/components/shared/DocumentCatalogCaption';
import { findAttachmentByDocTipo } from '@/lib/documents/doc-tipo';
import { formatFechaHora } from '@/lib/format/date';
import {
  ATTACHMENT_SOURCE_LABELS,
  type ChecklistItemView,
  type ProcedureAttachment,
} from '@/lib/api/types/procedure-runtime';
import {
  SeccionCargando,
  SeccionError,
  SeccionVacia,
  TarjetaDetalle,
  DetalleBadgeSoft,
  type SeccionDetalleProps,
} from './primitivos';
import { DETALLE_BLUE, DETALLE_GREEN, DETALLE_GOLD, DETALLE_GREY } from './detalle-visual';

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
 *
 * HU #12411 — modo consulta (trámite de un cliente hijo abierto por la cabeza de red): la sección
 * lista SOLO los adjuntos por la ruta proxeada `network/**` (contrato B5 #12410) — no hay checklist
 * de red, y la ruta propia respondería 403 — y ofrece únicamente «Ver» y «Descargar» por esa misma
 * ruta. Nunca `preview-url`, nunca cargar/reemplazar/regenerar/eliminar. Un 403/404 es «fuera de tu
 * alcance», sin reintento. Un 503 `audit_unavailable` (auditoría fail-closed de la descarga) es
 * transitorio: alerta con «Reintentar», sin visor ni binario.
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
          <DocumentCatalogCaption nombre={item.label} codigo={item.docTipo ?? item.key} />
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
          {formatBytes(attachment.sizeBytes)} · {formatFechaHora(attachment.uploadedAt)}
        </span>
      </span>
      <DownloadButton filename={attachment.filename} onClick={() => onDownload(attachment)} />
    </li>
  );
}

/**
 * HU #12411 — fila de un adjunto de la red: metadatos + «Ver» y «Descargar» (nada más). Los dos
 * botones llevan el nombre del archivo en su nombre accesible para que, navegando por lista de
 * botones, «Ver soat.pdf» y «Ver fur.pdf» sean distinguibles.
 */
function ConsultaRow({
  attachment,
  onVer,
  onDescargar,
}: {
  attachment: ProcedureAttachment;
  onVer: (attachment: ProcedureAttachment) => void;
  onDescargar: (attachment: ProcedureAttachment) => void;
}) {
  const origen = ATTACHMENT_SOURCE_LABELS[attachment.source] ?? attachment.source;
  return (
    <li className="flex items-center justify-between gap-2 rounded-xl border px-3 py-2 border-[#DFE5ED] dark:border-white/10">
      <span className="min-w-0">
        <span className="block truncate text-xs font-medium text-[#162744] dark:text-white">
          {documentLabel(attachment.tipo)} · {attachment.filename}
        </span>
        <span className="block text-xs text-[#162744]/70 dark:text-white/70">
          {formatBytes(attachment.sizeBytes)} · {formatFechaHora(attachment.uploadedAt)}
          {origen ? ` · ${origen}` : ''}
        </span>
      </span>
      <span className="flex shrink-0 items-center gap-1.5">
        <button
          type="button"
          onClick={() => onVer(attachment)}
          aria-label={`Ver ${attachment.filename}`}
          title="Ver"
          className="shrink-0 rounded-lg border p-1.5 transition hover:bg-[#557EFF]/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 border-[#DFE5ED] dark:border-white/10"
          style={{ color: AZUL }}
        >
          <Eye className="h-3.5 w-3.5" aria-hidden="true" />
        </button>
        <DownloadButton filename={attachment.filename} onClick={() => onDescargar(attachment)} />
      </span>
    </li>
  );
}

/** Rótulo textual del modo consulta en la cabecera de la tarjeta (no depende del color). */
function RotuloSoloConsulta() {
  return (
    <span
      className="inline-flex shrink-0 items-center gap-1 whitespace-nowrap rounded-full px-2.5 py-0.5 text-[10px] font-semibold"
      style={{ background: `${DETALLE_GREY}22`, color: '#475569' }}
    >
      <Eye className="h-3 w-3" aria-hidden="true" />
      {ETIQUETA_SOLO_CONSULTA}
    </span>
  );
}

/**
 * HU #12411 — sección «Documentos» en modo consulta. Es un componente aparte para que el camino
 * propio (`TramiteDetalleDocumentosPropio`) quede como hoy: mismas llamadas, misma UI (AC4/AC5).
 */
function TramiteDetalleDocumentosConsulta({ instanceId, tenantId }: SeccionDetalleProps) {
  const [attachments, setAttachments] = useState<ProcedureAttachment[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [fueraDeAlcance, setFueraDeAlcance] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);

  // Ver/Descargar por la ruta de red, nunca `preview-url` (AC2).
  const preview = useAttachmentPreview(instanceId, tenantId, { consultaMode: true });

  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      setLoading(true);
      setError(null);
      setFueraDeAlcance(false);
      try {
        // contrato B5 #12410
        const list = await tramitesClient.getNetworkAttachments(instanceId);
        if (!cancelled) setAttachments(list ?? []);
      } catch (e: unknown) {
        if (!cancelled) {
          const d = describirErrorDeSeccion(
            e,
            true,
            'No se pudieron cargar los documentos del trámite.',
            COPY_DOCUMENTOS_FUERA_DE_ALCANCE,
          );
          setError(d.mensaje);
          setFueraDeAlcance(d.fueraDeAlcance);
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
  }, [instanceId, reloadKey]);

  const titulo = 'Documentos del trámite';

  if (loading) {
    return (
      <TarjetaDetalle titulo={titulo} accion={<RotuloSoloConsulta />}>
        <SeccionCargando etiqueta="Cargando documentos del trámite" />
      </TarjetaDetalle>
    );
  }

  if (error) {
    return (
      <TarjetaDetalle titulo={titulo} accion={<RotuloSoloConsulta />}>
        <SeccionError
          mensaje={error}
          contexto="los documentos del trámite"
          // AC3 — rechazo de alcance: estado (no alerta) y sin reintento.
          sinReintento={fueraDeAlcance}
          onReintentar={() => setReloadKey((k) => k + 1)}
        />
      </TarjetaDetalle>
    );
  }

  if (attachments.length === 0) {
    return (
      <TarjetaDetalle titulo={titulo} accion={<RotuloSoloConsulta />}>
        <SeccionVacia mensaje="Este trámite no tiene documentos registrados." />
      </TarjetaDetalle>
    );
  }

  return (
    <div className="flex flex-col gap-4">
      <TarjetaDetalle titulo={titulo} className="h-full" accion={<RotuloSoloConsulta />}>
        <ul
          className="grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3"
          aria-label="Documentos del trámite (solo consulta)"
        >
          {attachments.map((a) => (
            <ConsultaRow
              key={a.id}
              attachment={a}
              onVer={(att) => void preview.open(att)}
              onDescargar={(att) => void preview.download(att)}
            />
          ))}
        </ul>
      </TarjetaDetalle>

      {/* 403/404 de alcance: solo texto. 503 audit_unavailable: texto + «Reintentar». */}
      <AvisoDescargaFallida preview={preview} />

      <AttachmentPreview preview={preview} />
    </div>
  );
}

export function TramiteDetalleDocumentos(props: SeccionDetalleProps) {
  /**
   * HU #12362 (AC3) / HU #12411 — modo consulta: la sección solo CONSULTA por las rutas de red.
   * Se bifurca ANTES de cualquier efecto para que el camino propio no cambie ni una llamada.
   */
  const consultaMode = useConsultaMode();
  if (consultaMode) return <TramiteDetalleDocumentosConsulta {...props} />;
  return <TramiteDetalleDocumentosPropio {...props} />;
}

function TramiteDetalleDocumentosPropio({ instanceId, tenantId }: SeccionDetalleProps) {
  const [checklist, setChecklist] = useState<ChecklistItemView[]>([]);
  const [attachments, setAttachments] = useState<ProcedureAttachment[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fueraDeAlcance, setFueraDeAlcance] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);
  const preview = useAttachmentPreview(instanceId, tenantId);

  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      setLoading(true);
      setError(null);
      setFueraDeAlcance(false);
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
          // Camino propio: nunca en consulta (la bifurcación vive en `TramiteDetalleDocumentos`).
          const d = describirErrorDeSeccion(
            e,
            false,
            'No se pudieron cargar los documentos del trámite.',
            COPY_DOCUMENTOS_FUERA_DE_ALCANCE,
          );
          setError(d.mensaje);
          setFueraDeAlcance(d.fueraDeAlcance);
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

  const satisfiedCount = checklist.filter((i) => i.satisfied).length;
  const checklistDocTipos = new Set(
    checklist.map((i) => i.docTipo?.toLowerCase()).filter(Boolean),
  );
  const otrosAdjuntos = attachments.filter(
    (a) => a.source !== 'system' && !checklistDocTipos.has(a.tipo.toLowerCase()),
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
        <SeccionError
          mensaje={error}
          sinReintento={fueraDeAlcance}
          onReintentar={() => setReloadKey((k) => k + 1)}
        />
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
      <TarjetaDetalle
        titulo="Documentos del trámite"
        className="h-full"
        accion={
          <span className="shrink-0 text-xs font-semibold text-[#162744] dark:text-white">
            {satisfiedCount} de {checklist.length} requisitos cumplidos
          </span>
        }
      >
        {checklist.length === 0 ? (
          <SeccionVacia mensaje="Este trámite no tiene requisitos de documentos configurados." />
        ) : (
          <ul className="grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3" aria-label="Requisitos del trámite">
            {checklist.map((item) => {
              const attachment = findAttachmentByDocTipo(attachments, item.docTipo);
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
              <AdjuntoRow
                key={a.id}
                attachment={a}
                onDownload={(att) => void preview.download(att)}
              />
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
