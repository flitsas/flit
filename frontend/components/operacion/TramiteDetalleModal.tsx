'use client';

import { useEffect, useState } from 'react';
import {
  Coins,
  Download,
  FileCheck2,
  FileText,
  FolderCheck,
  Users,
  X,
} from 'lucide-react';
import { Modal } from '@/components/atom/Modal';
import { InlineAlert } from '@/components/atom/InlineAlert';
import { StepMarker } from './WizardStepTracker';
import { plateFlowHint } from './TramitesTable';
import { AttachmentPreview, useAttachmentPreview } from './TramiteDocumentosModal';
import { SeccionCargando, SeccionError } from './detalle/primitivos';
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
import { estadoChipStyle, estadoLabel } from '@/lib/tramites/estados';
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

const BLUE = '#557EFF';
const NAVY = '#162744';

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

export interface TramiteDetalleModalProps {
  open: boolean;
  onClose: () => void;
  instanceId: string | null;
  tenantId?: string;
  item: InstanceSummary | null;
}

export function TramiteDetalleModal({
  open,
  onClose,
  instanceId,
  tenantId,
  item,
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
  const chip = item ? estadoChipStyle(item.estado) : null;
  const chipLabel = item ? estadoLabel(item.estado) : '';
  const plateHint = item ? plateFlowHint(item.plateFlowStatus) : null;
  const systemAttachments = attachments.filter((a) => a.source === 'system');

  const toggleTracking = (panel: Exclude<PanelTracking, null>) => {
    setPanelTracking((prev) => (prev === panel ? null : panel));
  };

  const selectSeccion = (id: SeccionId) => {
    setSeccion(id);
    setPanelTracking(null);
  };

  const header = item
    ? ({ titleId }: { titleId: string }) => (
    <div className="flex flex-wrap items-start justify-between gap-3 px-1 py-1">
      <div className="min-w-0">
        <div className="flex flex-wrap items-center gap-2">
          <h2
            id={titleId}
            className="text-[22px] font-bold leading-tight text-[#557EFF] dark:text-[#557EFF]"
          >
            {title}
          </h2>
          {chip ? (
            <span
              className="inline-flex items-center rounded-full px-3 py-1 text-[11px] font-semibold text-white"
              style={{ background: chip.accent }}
            >
              {chipLabel}
            </span>
          ) : null}
        </div>
        <p className="mt-1 text-[12px] text-slate-600 dark:text-white/70">
          <span className="font-mono">{item.referenceNumber}</span> · {item.placa ?? '—'} ·{' '}
          {item.gestorNombre ?? '—'}
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
                  : { borderColor: '#DFE5ED', color: NAVY }
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
          className="rounded-xl border border-[#DFE5ED] bg-white p-2 dark:border-white/10 dark:bg-[#162744]"
        >
          <X className="h-4 w-4" aria-hidden="true" />
        </button>
      </div>
    </div>
    )
    : undefined;

  return (
    <>
      <Modal
        open={open}
        onClose={onClose}
        title={title}
        size="2xl"
        header={header}
        panelClassName="border-0 bg-[#EEF5FF] p-5 dark:bg-[#05060A] sm:p-5"
        bodyClassName="overflow-visible"
        zClassName="z-[1100]"
      >
        {!item ? (
          <p className="py-6 text-center text-xs opacity-70">No se encontró información del trámite.</p>
        ) : (
          <div className="flex flex-col gap-3">
            {item.ultimoRechazoMotivo?.trim() ? (
              <InlineAlert tone="error" title="Rechazado por el Organismo de Tránsito">
                {item.ultimoRechazoMotivo.trim()}
              </InlineAlert>
            ) : null}
            {item.subsanacionActiva ? (
              <InlineAlert tone="warning" title="En subsanación">
                Este trámite tiene una subsanación activa: se está editando sin volver a borrador.
              </InlineAlert>
            ) : null}
            {item.isPaused ? (
              <InlineAlert tone="warning" title="Trámite pausado">
                {item.pausedObservation?.trim() || 'Este trámite está pausado y no avanza hasta reanudarlo.'}
              </InlineAlert>
            ) : null}
            {plateHint ? <InlineAlert tone="info">{plateHint}</InlineAlert> : null}

            <div
              role="tablist"
              aria-label="Pasos del trámite"
              className="flex items-start overflow-x-auto rounded-[18px] border bg-white p-4 dark:bg-[#162744] border-[#DFE5ED] dark:border-white/10"
            >
              {pasos.map((paso, i) => {
                const activa = paso.id === seccion && panelTracking === null;
                const esUltimo = i === pasos.length - 1;
                const completo = esUltimo ? item.estado === 'aprobado' : true;
                return (
                  <div key={paso.id} className="flex flex-1 items-center last:flex-none">
                    <button
                      type="button"
                      role="tab"
                      id={`detalle-tab-${paso.id}`}
                      aria-selected={activa}
                      aria-controls={`detalle-panel-${paso.id}`}
                      aria-label={`Paso ${i + 1}: ${paso.label} — ${completo ? 'completado' : 'pendiente'}`}
                      onClick={() => selectSeccion(paso.id)}
                      className="flex shrink-0 flex-col items-center gap-1.5 rounded-xl px-2 py-1 transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
                      style={activa ? { boxShadow: '0 0 0 1.5px #557EFF' } : undefined}
                    >
                      <StepMarker
                        status={completo ? 'complete' : 'incomplete'}
                        index={i}
                        active={activa}
                      />
                      <span
                        className="whitespace-nowrap text-xs font-medium"
                        style={{ color: activa ? BLUE : completo ? NAVY : '#59677D' }}
                      >
                        {paso.label}
                      </span>
                    </button>
                    {!esUltimo ? (
                      <div
                        className="mx-1 mt-[-18px] h-0.5 flex-1 rounded-full"
                        style={{ background: completo ? '#8CC63F' : '#DFE5ED' }}
                        aria-hidden="true"
                      />
                    ) : null}
                  </div>
                );
              })}
            </div>

            <div className="grid grid-cols-1 items-stretch gap-4 lg:grid-cols-12">
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
                      className="flex min-w-0 flex-col gap-4"
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
                            <div className="rounded-[18px] border bg-white p-4 dark:bg-[#162744] border-[#DFE5ED] dark:border-white/10">
                              <h4 className="mb-3 text-sm font-bold" style={{ color: NAVY }}>
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
                            </div>
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
                              <TimelineTrackPanel
                                title="Historial de auditoría"
                                nodes={mapStatusHistoryToTimelineNodes(detail?.statusHistory ?? [])}
                                emptyMessage="Sin eventos registrados todavía."
                              />
                            )}
                            <p className="mt-2 font-mono text-[10px] opacity-60">
                              Radicado: {item.referenceNumber}
                            </p>
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
      </Modal>

      <AttachmentPreview preview={preview} />
    </>
  );
}
