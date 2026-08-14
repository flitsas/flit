'use client';

import { useEffect, useState } from 'react';
import { Clock, Coins, Download, FileText, FolderCheck, Users } from 'lucide-react';
import { Modal } from '@/components/atom/Modal';
import { InlineAlert } from '@/components/atom/InlineAlert';
import { StatusBadge } from '@/components/atom/StatusBadge';
import ExpedienteTimeline from './ExpedienteTimeline';
import { plateFlowHint } from './TramitesTable';
import { AttachmentPreview, useAttachmentPreview } from './TramiteDocumentosModal';
// Vocabulario compartido por TODAS las secciones del detalle. El modal usa las mismas piezas que
// ellas: tenía un esqueleto y un error propios, escritos antes de que existieran las primitivas,
// y dos versiones del mismo estado es exactamente la duplicación que la norma prohíbe.
import { SeccionCargando, SeccionError } from './detalle/primitivos';
import { TramiteDetalleDocumentos } from './detalle/TramiteDetalleDocumentos';
import { TramiteDetalleActores } from './detalle/TramiteDetalleActores';
import { TramiteDetalleVehiculo } from './detalle/TramiteDetalleVehiculo';
import { TramiteDetalleComercial } from './detalle/TramiteDetalleComercial';
import { TramiteDetalleIdentidad } from './detalle/TramiteDetalleIdentidad';
import { tramitesClient } from '@/lib/api/tramites-client';
import { estadoChipStyle, estadoLabel } from '@/lib/tramites/estados';
import { formatFecha } from '@/lib/format/date';
import type {
  InstanceSummary,
  ProcedureAttachment,
  ProcedureInstanceDetail,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

/**
 * Frente C, etapa 1 — armazón del modal de detalle del trámite (cabecera + tarjeta de vehículo +
 * trazabilidad). Se abre desde el listado para todo trámite YA RADICADO (estado ≠ 'borrador'); el
 * borrador sigue navegando al asistente (ver `TramitesTable`). Reemplaza al detalle de la propuesta
 * (`DetalleTramiteModal.tsx`), del que se toma solo la COMPOSICIÓN: los valores salen de
 * `lib/tramites/estados.ts`, `globals.css` y el token file FLIT — nunca los hex/opacidades/badges
 * sólidos que la propuesta improvisó.
 *
 * Las secciones de actores, documentos, comerciales y prevuelo llegan en etapas siguientes; aquí solo
 * se monta la trazabilidad (`ExpedienteTimeline`, ya escrito y hasta ahora nunca montado) y los
 * archivos finales generados por el sistema.
 */

const BORDER = '#DFE5ED';
const BLUE = '#557EFF';
const NAVY = '#162744';

const MODALIDAD_TITLE: Record<WizardModalidad, string> = {
  matricula_inicial: 'Detalle de matrícula inicial',
  traspaso: 'Detalle de traspaso',
};

type SeccionId = 'trazabilidad' | 'documentos' | 'actores' | 'vehiculo' | 'comercial';

/**
 * Secciones del detalle. Los iconos son los mismos que la propuesta asigna a cada bloque en su
 * stepper; lo que cambia es que aquí son PESTAÑAS de contenido y no pasos con progreso.
 */
const SECCIONES: { id: SeccionId; label: string; Icon: typeof Clock }[] = [
  { id: 'trazabilidad', label: 'Trazabilidad', Icon: Clock },
  { id: 'documentos', label: 'Documentos', Icon: FolderCheck },
  { id: 'actores', label: 'Actores y firmas', Icon: Users },
  { id: 'vehiculo', label: 'Vehículo', Icon: FileText },
  { id: 'comercial', label: 'Comercial y prenda', Icon: Coins },
];

function estadoChip(estado: InstanceSummary['estado']) {
  const style = estadoChipStyle(estado);
  return { label: estadoLabel(estado), ...style };
}

export interface TramiteDetalleModalProps {
  open: boolean;
  onClose: () => void;
  /** `null` mientras no hay trámite elegido; el `Modal` no renderiza nada si `open` es false. */
  instanceId: string | null;
  /** Tenant de la fila — solo lo envía el SuperAdmin (ve trámites de otras compañías). */
  tenantId?: string;
  /**
   * Resumen de la fila (lo que ya se tiene sin esperar red): cabecera y tarjeta de vehículo se
   * pintan con esto de inmediato, incluso mientras `getInstance` sigue en vuelo.
   */
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

  // Sección activa del navegador. Se reinicia al cambiar de trámite: abrir otro expediente y
  // encontrarse en la pestaña donde quedó el anterior desorienta más de lo que ahorra. El reinicio
  // se hace AJUSTANDO EL ESTADO DURANTE EL RENDER (patrón de React para "resetear al cambiar una
  // prop"), no desde un efecto: con el efecto se pintaría un fotograma con la pestaña vieja.
  const [seccion, setSeccion] = useState<SeccionId>('trazabilidad');
  const [seccionDe, setSeccionDe] = useState<string | null>(instanceId);
  if (instanceId !== seccionDe) {
    setSeccionDe(instanceId);
    setSeccion('trazabilidad');
  }

  const preview = useAttachmentPreview(instanceId, tenantId);

  // Única llamada de esta etapa: dispara al abrirse, se cancela si se cierra o cambia el trámite.
  // Mismo patrón que `TramiteDocumentosModal` — el `load` nombrado evita el cascading-render lint
  // de llamar setState directo en el cuerpo del efecto.
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

  // Archivos finales — llamada aparte (GET attachments), filtrada a `source === 'system'` al pintar.
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

  const title = item ? MODALIDAD_TITLE[item.modalidad] : 'Detalle del trámite';
  const chip = item ? estadoChip(item.estado) : null;
  const plateHint = item ? plateFlowHint(item.plateFlowStatus) : null;
  const systemAttachments = attachments.filter((a) => a.source === 'system');

  return (
    <>
      <Modal
        open={open}
        onClose={onClose}
        title={title}
        size="2xl"
        description={
          item ? (
            <span className="flex flex-col gap-1.5">
              {chip ? (
                <StatusBadge label={chip.label} bg={chip.bg} color={chip.color} border={chip.border} />
              ) : null}
              <span className="text-xs opacity-70">
                <span className="font-mono">{item.referenceNumber}</span> · {item.placa ?? '—'} ·{' '}
                {item.gestorNombre ?? '—'}
              </span>
            </span>
          ) : undefined
        }
      >
        {!item ? (
          <p className="py-6 text-center text-xs opacity-70">No se encontró información del trámite.</p>
        ) : (
          // Superficie tintada bajo el contenido: el token `background.modal` es #EEF5FF, no blanco,
          // y es lo que hace que las tarjetas interiores se lean COMO tarjetas. El átomo `Modal`
          // pinta su panel en blanco (deuda suya, compartida con el resto de la app), así que el
          // tinte se aplica aquí, en el cuerpo, en vez de cambiar el componente para todas las
          // pantallas. Sin esto son tarjetas blancas sobre blanco, separadas solo por su borde.
          <div className="flex flex-col gap-3 rounded-2xl bg-[#EEF5FF] p-3 dark:bg-white/[0.04]">
            {/* Banner contextual — se apila si hay varios; el orden va de más a menos urgente. */}
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

            <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
              {/* Izquierda — tarjeta de vehículo. Se pinta con el resumen de la fila: no depende de
                  `getInstance`, así que está lista desde el primer render (sin foto: no hay dato de
                  imagen en el contrato y no se inventa). */}
              <div
                className="h-fit rounded-[18px] border bg-white p-4 dark:bg-[#162744]"
                style={{ borderColor: BORDER }}
              >
                <p
                  className="text-center font-mono text-lg font-bold uppercase tracking-wider"
                  style={{ color: NAVY }}
                >
                  {item.placa || '—'}
                </p>
                <p className="mt-1 text-center text-xs opacity-70">
                  {[item.vehiculoMarca, item.vehiculoLinea].filter(Boolean).join(' ') || '—'}
                </p>
                <p className="mt-2 text-center font-mono text-xs opacity-70">
                  VIN: {item.vin ?? '—'}
                </p>
                <div className="mt-3 border-t pt-3 text-xs opacity-70" style={{ borderColor: BORDER }}>
                  <p>Creado: {formatFecha(item.createdAt)}</p>
                  <p>Actualizado: {item.updatedAt ? formatFecha(item.updatedAt) : '—'}</p>
                </div>
              </div>

              {/* Derecha — navegador de secciones + la sección activa.
                  Es un `tablist`, NO el stepper del asistente: los pasos del wizard son cinco en
                  matrícula y seis en traspaso, y en un trámite ya radicado dejaron de significar
                  progreso. La propuesta reusa su stepper y marca los pasos completados con un
                  porcentaje inventado (`row.prog`); aquí eso mentiría.
                  Trazabilidad va primera y es la de por defecto: a un trámite radicado se entra a
                  ver en qué va y qué quedó adjunto, no a releer las especificaciones del vehículo.
                  Cada sección pide sus propios datos AL MONTARSE, así que abrir el modal no dispara
                  las siete llamadas de golpe: solo las de la sección que se está mirando. */}
              <div className="flex min-w-0 flex-col gap-4 lg:col-span-2">
                <div
                  role="tablist"
                  aria-label="Secciones del detalle"
                  className="flex flex-wrap items-center gap-1 border-b"
                  style={{ borderColor: BORDER }}
                >
                  {SECCIONES.map(({ id, label, Icon }) => {
                    const activa = id === seccion;
                    return (
                      <button
                        key={id}
                        type="button"
                        role="tab"
                        id={`detalle-tab-${id}`}
                        aria-selected={activa}
                        aria-controls={`detalle-panel-${id}`}
                        onClick={() => setSeccion(id)}
                        className="relative inline-flex items-center gap-1.5 rounded-t-lg px-3 py-2 text-xs font-semibold transition focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[#557EFF]"
                        style={activa ? { color: BLUE } : { color: NAVY, opacity: 0.7 }}
                      >
                        <Icon className="h-3.5 w-3.5" aria-hidden="true" />
                        {label}
                        {/* El activo se marca con color Y con subrayado: no depende solo del color. */}
                        {activa ? (
                          <span
                            className="absolute inset-x-2 -bottom-px h-0.5 rounded-full"
                            style={{ background: BLUE }}
                            aria-hidden="true"
                          />
                        ) : null}
                      </button>
                    );
                  })}
                </div>

                <div
                  role="tabpanel"
                  id={`detalle-panel-${seccion}`}
                  aria-labelledby={`detalle-tab-${seccion}`}
                  className="flex min-w-0 flex-col gap-4"
                >
                {seccion !== 'trazabilidad' && instanceId ? (
                  <>
                    {seccion === 'documentos' ? (
                      <TramiteDetalleDocumentos
                        instanceId={instanceId}
                        tenantId={tenantId}
                        item={item}
                      />
                    ) : null}
                    {seccion === 'actores' ? (
                      <TramiteDetalleActores instanceId={instanceId} tenantId={tenantId} item={item} />
                    ) : null}
                    {seccion === 'vehiculo' ? (
                      <TramiteDetalleVehiculo instanceId={instanceId} tenantId={tenantId} item={item} />
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

                {seccion === 'trazabilidad' ? (
                  <>
                {loading ? <SeccionCargando etiqueta="Cargando trazabilidad" filas={3} /> : null}
                {!loading && error ? (
                  <SeccionError
                    mensaje={error}
                    contexto="la trazabilidad"
                    onReintentar={() => setDetailReloadKey((k) => k + 1)}
                  />
                ) : null}
                {!loading && !error ? (
                  <ExpedienteTimeline statusHistory={detail?.statusHistory ?? []} />
                ) : null}

                <div
                  className="rounded-2xl border bg-white p-4 dark:bg-[#162744]"
                  style={{ borderColor: BORDER }}
                >
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
                          className="flex items-center justify-between gap-2 rounded-xl border px-3 py-2"
                          style={{ borderColor: BORDER }}
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
                            className="shrink-0 rounded-lg border p-1.5 transition hover:bg-[#557EFF]/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
                            style={{ borderColor: BORDER, color: BLUE }}
                          >
                            <Download className="h-3.5 w-3.5" aria-hidden="true" />
                          </button>
                        </li>
                      ))}
                    </ul>
                  ) : null}
                  {/* Fallo de una descarga directa: el visor no llega a abrirse, así que el aviso va
                      aquí (mismo patrón que `TramiteDocumentosModal`). */}
                  {preview.doc === null && preview.error ? (
                    <p className="mt-3 text-xs" style={{ color: '#FF4E00' }} role="alert">
                      {preview.error}
                    </p>
                  ) : null}
                </div>

                {/* Tercer bloque de la trazabilidad: el estado de la validación de identidad, por
                    parte. Vive aquí y no en su propia pestaña porque responde a la misma pregunta
                    que la cronología y los archivos: en qué va el expediente. */}
                {instanceId ? (
                  <TramiteDetalleIdentidad
                    instanceId={instanceId}
                    tenantId={tenantId}
                    item={item}
                  />
                ) : null}
                  </>
                ) : null}
                </div>
              </div>
            </div>
          </div>
        )}
      </Modal>

      <AttachmentPreview preview={preview} />
    </>
  );
}
