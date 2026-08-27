'use client';

import { useEffect, useState } from 'react';
import {
  AlertTriangle,
  Coins,
  Download,
  FileCheck2,
  FileText,
  FolderCheck,
  Loader2,
  PenLine,
  Users,
  X,
} from 'lucide-react';
import { InlineAlert } from '@/components/atom/InlineAlert';
import { plateFlowHint } from './TramitesTable';
import { AttachmentPreview, useAttachmentPreview } from './TramiteDocumentosModal';
import { SeccionCargando, SeccionError } from './detalle/primitivos';
import { DetalleTramiteShell } from './detalle/DetalleTramiteShell';
import { DetalleStepper } from './detalle/DetalleStepper';
import { DetalleHistorialAuditoria } from './detalle/DetalleHistorialAuditoria';
import { detalleEstadoHeader } from './detalle/detalle-estado-header';
import {
  DETALLE_BLUE,
  DETALLE_CARD,
  DETALLE_CTA_GRADIENT,
  DETALLE_NAVY,
  DETALLE_BORDER,
} from './detalle/detalle-visual';
import { TramiteDetalleDocumentos } from './detalle/TramiteDetalleDocumentos';
import { TramiteDetalleActores } from './detalle/TramiteDetalleActores';
import { TramiteDetalleVehiculo } from './detalle/TramiteDetalleVehiculo';
import { TramiteDetalleComercial } from './detalle/TramiteDetalleComercial';
import { TramiteDetalleIdentidad } from './detalle/TramiteDetalleIdentidad';
import { DetalleVehiculoSidebar } from './detalle/DetalleVehiculoSidebar';
import { TimelineTrackPanel } from './detalle/TimelineTrackPanel';
import {
  mapIdentidadToTimelineNodes,
  mapStatusHistoryToTimelineNodes,
} from './detalle/timeline-mappers';
import { tramitesClient } from '@/lib/api/tramites-client';
import type {
  BiometricValidation,
  InstanceSummary,
  ProcedureAttachment,
  ProcedureInstanceDetail,
} from '@/lib/api/types/procedure-runtime';
import type { ProcedureFamily } from '@/lib/api/types/procedure-parametrization';

/**
 * Modal «Ver» — detalle del trámite radicado. Shell alineado al mockup (`DetalleTramiteModal`):
 * canvas #EEF5FF, header compuesto, toggles de trazabilidad, grid 4/8 y stepper ADR-0050.
 */

const BLUE = DETALLE_BLUE;
const NAVY = DETALLE_NAVY;

const MODALIDAD_TITLE: Record<ProcedureFamily, string> = {
  OTROS: 'Otros trámites',
  MATRICULAS: 'Detalle de matrícula inicial',
  TRASPASO: 'Detalle de traspaso',
};

type SeccionId = 'vehiculo' | 'actores' | 'documentos' | 'comercial' | 'expediente';
type PanelTracking = 'identidad' | 'timeline' | null;

const PASOS_POR_MODALIDAD: Record<
  ProcedureFamily,
  { id: SeccionId; label: string; Icon: typeof FileText }[]
> = {
  TRASPASO: [
    { id: 'vehiculo', label: 'Trámite y vehículo', Icon: FileText },
    { id: 'actores', label: 'Actores y validación', Icon: Users },
    { id: 'documentos', label: 'Documentos', Icon: FolderCheck },
    { id: 'comercial', label: 'Datos comerciales', Icon: Coins },
    { id: 'expediente', label: 'FUR y expediente', Icon: FileCheck2 },
  ],
  MATRICULAS: [
    { id: 'vehiculo', label: 'Consulta VIN y placa', Icon: FileText },
    { id: 'actores', label: 'Comprador y rep. legal', Icon: Users },
    { id: 'documentos', label: 'Documentos', Icon: FolderCheck },
    { id: 'expediente', label: 'FUR y expediente', Icon: FileCheck2 },
  ],
  OTROS: [
    { id: 'vehiculo', label: 'Consulta VIN y placa', Icon: FileText },
    { id: 'actores', label: 'Comprador y rep. legal', Icon: Users },
    { id: 'documentos', label: 'Documentos', Icon: FolderCheck },
    { id: 'expediente', label: 'FUR y expediente', Icon: FileCheck2 },
  ],
};

function resolveTitle(item: InstanceSummary): string {
  if (item.modalidad === 'OTROS' && item.tipoNombre?.trim()) {
    return `Detalle de ${item.tipoNombre.trim().toLocaleLowerCase('es')}`;
  }
  return MODALIDAD_TITLE[item.modalidad];
}

/**
 * CTA de la subsanación dentro de un aviso del modal (`InlineAlert action`). Es la ÚNICA acción de
 * esta vista que cambia el trámite —el resto son conmutadores de vista y descargas—, así que va con
 * el degradado primario y no con el blanco de los toggles del encabezado.
 */
function BotonSubsanacion({
  label,
  loading,
  onClick,
}: {
  label: string;
  loading: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={loading}
      className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white transition hover:opacity-90 disabled:opacity-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-offset-2 focus-visible:ring-[#557EFF]"
      style={{ background: DETALLE_CTA_GRADIENT }}
      title="Abre el trámite en el asistente de pasos para corregirlo y volver a radicarlo"
    >
      {loading ? (
        <>
          <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
          Abriendo el asistente…
        </>
      ) : (
        <>
          <PenLine className="h-3.5 w-3.5" aria-hidden="true" />
          {label}
        </>
      )}
    </button>
  );
}

export interface TramiteDetalleModalProps {
  open: boolean;
  onClose: () => void;
  instanceId: string | null;
  tenantId?: string;
  item: InstanceSummary | null;
  /**
   * Lleva el trámite al asistente de pasos (`/tramites/{id}`), que es donde se edita. El modal no
   * navega por su cuenta —la ruta con `?t=` del SuperAdmin la resuelve el listado— y sin esta prop
   * simplemente no ofrece la acción de subsanar.
   */
  onAbrirAsistente?: (item: InstanceSummary) => void;
}

export function TramiteDetalleModal({
  open,
  onClose,
  instanceId,
  tenantId,
  item,
  onAbrirAsistente,
}: TramiteDetalleModalProps) {
  const [detail, setDetail] = useState<ProcedureInstanceDetail | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [detailReloadKey, setDetailReloadKey] = useState(0);

  const [attachments, setAttachments] = useState<ProcedureAttachment[]>([]);
  const [attLoading, setAttLoading] = useState(false);
  const [attError, setAttError] = useState<string | null>(null);
  const [attReloadKey, setAttReloadKey] = useState(0);

  const [panelTracking, setPanelTracking] = useState<PanelTracking>(null);

  // Activación/retoma de la subsanación (POST /subsanar + salto al asistente).
  const [abriendoSubsanacion, setAbriendoSubsanacion] = useState(false);
  const [subsanarError, setSubsanarError] = useState<string | null>(null);

  const [validations, setValidations] = useState<BiometricValidation[]>([]);
  const [firmaBaulPartes, setFirmaBaulPartes] = useState<string[]>([]);
  const [identidadLoading, setIdentidadLoading] = useState(false);
  const [identidadError, setIdentidadError] = useState<string | null>(null);
  const [identidadReloadKey, setIdentidadReloadKey] = useState(0);

  const [seccion, setSeccion] = useState<SeccionId>('expediente');
  const [seccionDe, setSeccionDe] = useState<string | null>(instanceId);
  if (instanceId !== seccionDe) {
    setSeccionDe(instanceId);
    setSeccion('expediente');
    setPanelTracking(null);
    // El modal se reutiliza entre trámites: sin esto un error de subsanación (o el "Abriendo…"
    // que quedó al navegar) reaparecería sobre el siguiente trámite que se abra.
    setAbriendoSubsanacion(false);
    setSubsanarError(null);
  }

  const preview = useAttachmentPreview(instanceId, tenantId);

  useEffect(() => {
    if (!open || !instanceId) return;
    let cancelled = false;
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const data = await tramitesClient.getInstance(instanceId, tenantId);
        if (!cancelled) setDetail(data ?? null);
      } catch (e: unknown) {
        if (!cancelled) {
          setError(e instanceof Error ? e.message : 'No se pudo cargar el trámite.');
          setDetail(null);
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    };
    void load();
    return () => {
      cancelled = true;
    };
  }, [open, instanceId, tenantId, detailReloadKey]);

  useEffect(() => {
    if (!open || !instanceId) return;
    let cancelled = false;
    const load = async () => {
      setAttLoading(true);
      setAttError(null);
      try {
        const list = await tramitesClient.getAttachments(instanceId, tenantId);
        if (!cancelled) setAttachments(list);
      } catch (e: unknown) {
        if (!cancelled) {
          setAttError(e instanceof Error ? e.message : 'No se pudieron cargar los archivos finales.');
          setAttachments([]);
        }
      } finally {
        if (!cancelled) setAttLoading(false);
      }
    };
    void load();
    return () => {
      cancelled = true;
    };
  }, [open, instanceId, tenantId, attReloadKey]);

  useEffect(() => {
    if (!open || !instanceId || panelTracking !== 'identidad') return;
    let cancelled = false;
    const load = async () => {
      setIdentidadLoading(true);
      setIdentidadError(null);
      try {
        const res = await tramitesClient.listBiometricExpediente(instanceId, tenantId);
        if (!cancelled) {
          setValidations(res.validations);
          setFirmaBaulPartes(res.firmaBaulPartes ?? []);
        }
      } catch (e: unknown) {
        if (!cancelled) {
          setIdentidadError(
            e instanceof Error ? e.message : 'No se pudo cargar la trazabilidad de identidad.',
          );
          setValidations([]);
          setFirmaBaulPartes([]);
        }
      } finally {
        if (!cancelled) setIdentidadLoading(false);
      }
    };
    void load();
    return () => {
      cancelled = true;
    };
  }, [open, instanceId, tenantId, panelTracking, identidadReloadKey]);

  const title = item ? resolveTitle(item) : 'Detalle del trámite';
  const pasos = item ? PASOS_POR_MODALIDAD[item.modalidad] : PASOS_POR_MODALIDAD.TRASPASO;
  const pasoActivoIndex = Math.max(
    0,
    pasos.findIndex((p) => p.id === seccion),
  );
  const pasoActivo = pasos[pasoActivoIndex];
  const StepIcon = pasoActivo?.Icon ?? FileText;
  const estadoHdr = item ? detalleEstadoHeader(item.estado) : null;
  const plateHint = item ? plateFlowHint(item.plateFlowStatus) : null;
  const systemAttachments = attachments.filter((a) => a.source === 'system');

  // Subsanación: `rechazado` es el único estado con vuelta a la edición (el backend responde 409
  // `not_rechazado` en cualquier otro). Con el flag ya encendido no se vuelve a activar: solo se
  // retoma. Sin `onAbrirAsistente` no se ofrece nada, porque activar sin poder editar deja peor.
  const subsanacionActiva = !!item?.subsanacionActiva;
  const puedeSubsanar =
    !!onAbrirAsistente && !!item && !!instanceId && item.estado === 'rechazado';
  const ofreceActivar = puedeSubsanar && !subsanacionActiva;
  const ofreceRetomar = puedeSubsanar && subsanacionActiva;

  /**
   * Enciende el flag si hace falta y salta al asistente. El POST es idempotente desde la UI: si la
   * subsanación ya está activa se omite, para que "Continuar" no choque contra el 409 de reactivar.
   */
  const irASubsanar = async () => {
    if (!item || !instanceId || abriendoSubsanacion) return;
    setAbriendoSubsanacion(true);
    setSubsanarError(null);
    try {
      if (!subsanacionActiva) await tramitesClient.startSubsanacion(instanceId, tenantId);
      onAbrirAsistente?.(item);
    } catch (err) {
      setSubsanarError(
        err instanceof Error ? err.message : 'No se pudo iniciar la subsanación.',
      );
      setAbriendoSubsanacion(false);
    }
  };

  const toggleTracking = (panel: Exclude<PanelTracking, null>) => {
    setPanelTracking((prev) => (prev === panel ? null : panel));
  };

  const selectSeccion = (id: SeccionId) => {
    setSeccion(id);
    setPanelTracking(null);
  };

  const pasosStepper = pasos.map((paso, i) => {
    const esUltimo = i === pasos.length - 1;
    return {
      id: paso.id,
      label: paso.label,
      Icon: paso.Icon,
      completo: esUltimo ? (item?.estado === 'aprobado') : true,
    };
  });

  const header = item
    ? ({ titleId }: { titleId: string }) => (
        <div className="flex flex-wrap items-start justify-between gap-3 px-1 py-2">
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-2">
              <h2
                id={titleId}
                className="text-[22px] font-bold leading-tight"
                style={{ color: BLUE }}
              >
                {title}
              </h2>
              {estadoHdr ? (
                <span
                  className="inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-[11px] font-semibold text-white"
                  style={{ background: estadoHdr.color }}
                >
                  <estadoHdr.Icon className="h-3.5 w-3.5" aria-hidden="true" />
                  {estadoHdr.label}
                </span>
              ) : null}
            </div>
            <p className="mt-1 text-[12px]" style={{ color: '#475569' }}>
              <span className="font-mono">{item.referenceNumber}</span> · {item.placa ?? '—'} ·
              Responsable: {item.gestorNombre ?? '—'}
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            {(
              [
                ['identidad', 'Trazabilidad de Identidad'],
                ['timeline', 'Línea de Tiempo del Trámite'],
              ] as const
            ).map(([key, label]) => {
              const active = panelTracking === key;
              return (
                <button
                  key={key}
                  type="button"
                  onClick={() => toggleTracking(key)}
                  aria-pressed={active}
                  className="rounded-xl border-2 px-4 py-2.5 text-xs font-semibold transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
                  style={
                    active
                      ? { background: BLUE, borderColor: BLUE, color: '#fff' }
                      : { borderColor: DETALLE_BORDER, color: NAVY, background: 'transparent' }
                  }
                >
                  {label}
                </button>
              );
            })}
            <button
              type="button"
              onClick={onClose}
              aria-label="Cerrar"
              className="rounded-xl border border-[#DFE5ED] bg-white p-2 dark:border-white/5 dark:bg-[#0B0F14]"
            >
              <X className="h-4 w-4" aria-hidden="true" />
            </button>
          </div>
        </div>
      )
    : undefined;

  return (
    <>
      <DetalleTramiteShell open={open} onClose={onClose} title={title} header={header}>
        {!item ? (
          <p className="py-6 text-center text-xs opacity-70">No se encontró información del trámite.</p>
        ) : (
          <div className="flex flex-col gap-3">
            {/* El rechazo es el bloqueo, así que su aviso es también donde vive la salida: activar
                la subsanación. Si el trámite está rechazado pero el OT no dejó motivo, el aviso se
                pinta igual — sin él la acción no tendría dónde vivir. */}
            {item.ultimoRechazoMotivo?.trim() || ofreceActivar ? (
              <InlineAlert
                tone="error"
                title="Rechazado por el Organismo de Tránsito"
                action={
                  ofreceActivar ? (
                    <BotonSubsanacion
                      label="Subsanar trámite"
                      loading={abriendoSubsanacion}
                      onClick={() => void irASubsanar()}
                    />
                  ) : undefined
                }
              >
                {item.ultimoRechazoMotivo?.trim() ??
                  'El organismo devolvió el trámite sin registrar un motivo. Actívale la subsanación para corregirlo y volver a radicarlo.'}
                {subsanarError ? (
                  <span className="mt-1 block font-semibold">{subsanarError}</span>
                ) : null}
              </InlineAlert>
            ) : null}
            {item.subsanacionActiva ? (
              <InlineAlert
                tone="warning"
                title="En subsanación"
                action={
                  ofreceRetomar ? (
                    <BotonSubsanacion
                      label="Continuar la subsanación"
                      loading={abriendoSubsanacion}
                      onClick={() => void irASubsanar()}
                    />
                  ) : undefined
                }
              >
                Este trámite tiene una subsanación activa: se está editando sin volver a borrador.
                {subsanarError && !ofreceActivar ? (
                  <span className="mt-1 block font-semibold">{subsanarError}</span>
                ) : null}
              </InlineAlert>
            ) : null}
            {item.isPaused ? (
              <InlineAlert tone="warning" title="Trámite pausado">
                {item.pausedObservation?.trim() || 'Este trámite está pausado y no avanza hasta reanudarlo.'}
              </InlineAlert>
            ) : null}
            {plateHint ? <InlineAlert tone="info">{plateHint}</InlineAlert> : null}
            {estadoHdr?.alert &&
            !item.ultimoRechazoMotivo?.trim() &&
            // Rechazado sin motivo ya se anuncia arriba, en el aviso que trae "Subsanar trámite":
            // sin esto saldrían dos avisos seguidos diciendo lo mismo.
            !ofreceActivar &&
            !item.subsanacionActiva &&
            !item.isPaused ? (
              <div
                className="mt-2 flex items-center gap-2 rounded-xl px-4 py-2.5"
                style={
                  estadoHdr.pendiente
                    ? { background: '#FEF9E7', border: '1px solid #F7E3A1' }
                    : {
                        background: `${estadoHdr.color}1F`,
                        border: `1px solid ${estadoHdr.color}55`,
                      }
                }
              >
                <AlertTriangle
                  className="h-4 w-4 shrink-0"
                  style={{ color: estadoHdr.pendiente ? '#B7791F' : estadoHdr.color }}
                  aria-hidden="true"
                />
                <p
                  className="text-xs font-medium"
                  style={{ color: estadoHdr.pendiente ? '#8A5E12' : estadoHdr.color }}
                >
                  {estadoHdr.alert}
                </p>
              </div>
            ) : null}

            <DetalleStepper
              pasos={pasosStepper}
              pasoActivoId={panelTracking ? '' : seccion}
              onSelect={(id) => selectSeccion(id as SeccionId)}
            />

            <div className="mt-3 grid grid-cols-1 items-stretch gap-4 lg:grid-cols-12">
              {panelTracking === 'timeline' ? (
                <div className="lg:col-span-12">
                  {loading ? (
                    <SeccionCargando etiqueta="Cargando línea de tiempo" filas={3} />
                  ) : error ? (
                    <SeccionError
                      mensaje={error}
                      contexto="la línea de tiempo"
                      onReintentar={() => setDetailReloadKey((k) => k + 1)}
                    />
                  ) : (
                    <TimelineTrackPanel
                      title="Línea de tiempo del trámite"
                      nodes={mapStatusHistoryToTimelineNodes(detail?.statusHistory ?? [])}
                      emptyMessage="Sin eventos registrados todavía."
                    />
                  )}
                </div>
              ) : panelTracking === 'identidad' ? (
                <div className="lg:col-span-12">
                  {identidadLoading ? (
                    <SeccionCargando etiqueta="Cargando trazabilidad de identidad" filas={3} />
                  ) : identidadError ? (
                    <SeccionError
                      mensaje={identidadError}
                      contexto="la trazabilidad de identidad"
                      onReintentar={() => setIdentidadReloadKey((k) => k + 1)}
                    />
                  ) : (
                    <TimelineTrackPanel
                      title="Trazabilidad de identidad"
                      nodes={mapIdentidadToTimelineNodes(
                        item.modalidad,
                        validations,
                        firmaBaulPartes,
                      )}
                      emptyMessage="Este trámite todavía no tiene validación de identidad iniciada."
                    />
                  )}
                </div>
              ) : (
                <>
                  <div className="lg:col-span-4">
                    <DetalleVehiculoSidebar item={item} />
                  </div>

                  <div className="flex min-w-0 flex-col gap-4 lg:col-span-8">
                    <div className="flex items-center gap-2">
                      <StepIcon className="h-4 w-4 shrink-0" style={{ color: BLUE }} aria-hidden="true" />
                      <h3 className="text-sm font-bold" style={{ color: BLUE }}>
                        {pasoActivoIndex + 1}. {pasoActivo?.label}
                      </h3>
                    </div>

                    <div
                      role="tabpanel"
                      id={`detalle-panel-${seccion}`}
                      aria-labelledby={`detalle-tab-${seccion}`}
                      className={`flex min-w-0 flex-col gap-4 ${seccion !== 'expediente' ? '[&>*]:h-full' : ''}`}
                    >
                      {seccion !== 'expediente' && instanceId ? (
                        <>
                          {seccion === 'documentos' ? (
                            <TramiteDetalleDocumentos
                              instanceId={instanceId}
                              tenantId={tenantId}
                              item={item}
                            />
                          ) : null}
                          {seccion === 'actores' ? (
                            <>
                              <TramiteDetalleActores
                                instanceId={instanceId}
                                tenantId={tenantId}
                                item={item}
                              />
                              <TramiteDetalleIdentidad
                                instanceId={instanceId}
                                tenantId={tenantId}
                                item={item}
                              />
                            </>
                          ) : null}
                          {seccion === 'vehiculo' ? (
                            <TramiteDetalleVehiculo
                              instanceId={instanceId}
                              tenantId={tenantId}
                              item={item}
                            />
                          ) : null}
                          {seccion === 'comercial' ? (
                            <TramiteDetalleComercial
                              instanceId={instanceId}
                              tenantId={tenantId}
                              item={item}
                            />
                          ) : null}
                        </>
                      ) : null}

                      {seccion === 'expediente' ? (
                        <div className="grid grid-cols-1 items-stretch gap-4 md:grid-cols-2">
                          <div className="flex flex-col gap-4">
                            {instanceId ? (
                              <TramiteDetalleIdentidad
                                instanceId={instanceId}
                                tenantId={tenantId}
                                item={item}
                              />
                            ) : null}
                            <section className={`${DETALLE_CARD}`}>
                              <h4 className="mb-3 text-sm font-semibold" style={{ color: NAVY }}>
                                Archivos finales
                              </h4>
                              {attLoading ? (
                                <SeccionCargando etiqueta="Cargando archivos finales" filas={2} />
                              ) : null}
                              {!attLoading && attError ? (
                                <SeccionError
                                  mensaje={attError}
                                  contexto="los archivos finales"
                                  onReintentar={() => setAttReloadKey((k) => k + 1)}
                                />
                              ) : null}
                              {!attLoading && !attError && systemAttachments.length === 0 ? (
                                <p className="text-xs opacity-70">
                                  Este trámite aún no tiene archivos finales generados por el sistema.
                                </p>
                              ) : null}
                              {!attLoading && !attError && systemAttachments.length > 0 ? (
                                <ul className="space-y-2" aria-label="Archivos finales del trámite">
                                  {systemAttachments.map((a) => (
                                    <li
                                      key={a.id}
                                      className="flex items-center justify-between gap-2 rounded-xl border px-3 py-2 border-[#DFE5ED] dark:border-white/10"
                                    >
                                      <span className="min-w-0">
                                        <span className="block truncate text-xs font-medium">{a.filename}</span>
                                        <span className="block truncate font-mono text-xs opacity-70">
                                          SHA-256 · {a.sha256}
                                        </span>
                                      </span>
                                      <button
                                        type="button"
                                        onClick={() => void preview.download(a)}
                                        aria-label={`Descargar ${a.filename}`}
                                        title="Descargar"
                                        className="shrink-0 rounded-lg border p-1.5 transition hover:bg-[#557EFF]/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] border-[#DFE5ED] dark:border-white/10"
                                        style={{ color: BLUE }}
                                      >
                                        <Download className="h-3.5 w-3.5" aria-hidden="true" />
                                      </button>
                                    </li>
                                  ))}
                                </ul>
                              ) : null}
                              {preview.doc === null && preview.error ? (
                                <p className="mt-3 text-xs" style={{ color: '#FF4E00' }} role="alert">
                                  {preview.error}
                                </p>
                              ) : null}
                            </section>
                          </div>

                          <div className="h-full min-h-0">
                            {loading ? (
                              <SeccionCargando etiqueta="Cargando historial" filas={3} />
                            ) : error ? (
                              <SeccionError
                                mensaje={error}
                                contexto="el historial de auditoría"
                                onReintentar={() => setDetailReloadKey((k) => k + 1)}
                              />
                            ) : (
                              <DetalleHistorialAuditoria
                                statusHistory={detail?.statusHistory ?? []}
                                referenceNumber={item.referenceNumber}
                              />
                            )}
                          </div>
                        </div>
                      ) : null}
                    </div>
                  </div>
                </>
              )}
            </div>
          </div>
        )}
      </DetalleTramiteShell>

      <AttachmentPreview preview={preview} />
    </>
  );
}
