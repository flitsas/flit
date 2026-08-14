'use client';

import { useEffect, useState } from 'react';
import {
  Clock,
  Coins,
  Download,
  FileCheck2,
  FileText,
  FolderCheck,
  ShieldCheck,
  Users,
} from 'lucide-react';
import { Modal } from '@/components/atom/Modal';
import { InlineAlert } from '@/components/atom/InlineAlert';
import { StatusBadge } from '@/components/atom/StatusBadge';
import ExpedienteTimeline from './ExpedienteTimeline';
// El círculo del stepper es el MISMO del asistente: verde con check si está cumplido, número azul
// con halo si es el activo, contorno gris si está pendiente. Una segunda versión aquí habría hecho
// que los dos steppers del producto se separaran con el tiempo.
import { StepMarker } from './WizardStepTracker';
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

type SeccionId = 'vehiculo' | 'actores' | 'documentos' | 'comercial' | 'expediente';

/**
 * Pasos del detalle. NO son una invención de esta pantalla: son la secuencia canónica que fija la
 * norma FLIT (`prototype_rules.md`, «Reglas de wizards y trámites») y que la propuesta dibuja tal
 * cual en su stepper —«Prohibido reordenar o renombrar pasos sin HU que lo respalde»—:
 *
 *   trámite general   Trámite y Vehículo → Actores y Validación → Documentos → Datos Comerciales → FUR y Expediente
 *   matrícula inicial Consulta VIN y Placa → Comprador y Rep. Legal → Documentos → FUR y Expediente
 *
 * De ahí que la matrícula inicial tenga CUATRO pasos y el traspaso cinco: la matrícula no tiene
 * datos comerciales. Lo que NO se copia de la propuesta es su marca de progreso, que sale de un
 * `row.prog` inventado; y aquí además no haría falta: este modal solo se abre para trámites YA
 * RADICADOS (el borrador va al asistente), así que los pasos de captura están cumplidos por
 * construcción y lo único que varía es dónde está el expediente, que es el último paso.
 */
const PASOS_POR_MODALIDAD: Record<
  WizardModalidad,
  { id: SeccionId; label: string; Icon: typeof Clock }[]
> = {
  traspaso: [
    { id: 'vehiculo', label: 'Trámite y vehículo', Icon: FileText },
    { id: 'actores', label: 'Actores y validación', Icon: Users },
    { id: 'documentos', label: 'Documentos', Icon: FolderCheck },
    { id: 'comercial', label: 'Datos comerciales', Icon: Coins },
    { id: 'expediente', label: 'FUR y expediente', Icon: FileCheck2 },
  ],
  matricula_inicial: [
    { id: 'vehiculo', label: 'Consulta VIN y placa', Icon: FileText },
    { id: 'actores', label: 'Comprador y rep. legal', Icon: Users },
    { id: 'documentos', label: 'Documentos', Icon: FolderCheck },
    { id: 'expediente', label: 'FUR y expediente', Icon: FileCheck2 },
  ],
};

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

  // Paso activo. Arranca en el ÚLTIMO —«FUR y expediente»— y no en el primero: el trámite ya está
  // radicado, así que ahí es literalmente donde está. Se reinicia al cambiar de trámite ajustando
  // el estado DURANTE EL RENDER (patrón de React para "resetear al cambiar una prop"), no desde un
  // efecto: con el efecto se pintaría un fotograma con el paso del trámite anterior.
  const [seccion, setSeccion] = useState<SeccionId>('expediente');
  const [seccionDe, setSeccionDe] = useState<string | null>(instanceId);
  if (instanceId !== seccionDe) {
    setSeccionDe(instanceId);
    setSeccion('expediente');
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
  // La matrícula inicial tiene CUATRO pasos y el traspaso cinco: es la secuencia de la norma, no
  // una simplificación (la matrícula no tiene datos comerciales).
  const pasos = item ? PASOS_POR_MODALIDAD[item.modalidad] : PASOS_POR_MODALIDAD.traspaso;
  // El paso por defecto («expediente») existe en las dos modalidades, así que el índice siempre
  // resuelve; el `Math.max` solo cubre un id que dejara de existir en un cambio futuro.
  const pasoActivoIndex = Math.max(
    0,
    pasos.findIndex((p) => p.id === seccion),
  );
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

            {/* Stepper horizontal, en su propia tarjeta y a todo el ancho, como en la propuesta y
                como manda la norma («círculos numerados y conector»). El marcador es el MISMO
                `StepMarker` del asistente, no una segunda versión.
                Marcar los pasos de captura como completados NO es progreso inventado: este modal
                solo se abre para trámites ya radicados, así que están cumplidos por construcción —
                por eso está radicado—. El último paso, «FUR y expediente», solo se da por completo
                cuando el trámite quedó aprobado; entregado, rechazado o anulado no lo están. */}
            <div
              role="tablist"
              aria-label="Pasos del trámite"
              className="flex items-start overflow-x-auto rounded-[18px] border bg-white p-4 dark:bg-[#162744]"
              style={{ borderColor: BORDER }}
            >
              {pasos.map((paso, i) => {
                const activa = paso.id === seccion;
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
                      // El estado va en el nombre accesible y no como texto oculto dentro del
                      // botón: así el círculo verde no es la única señal de "cumplido", y el
                      // rótulo visible sigue siendo solo el rótulo.
                      aria-label={`Paso ${i + 1}: ${paso.label} — ${completo ? 'completado' : 'pendiente'}`}
                      onClick={() => setSeccion(paso.id)}
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
                        {/* Sin el número delante: ya lo lleva el círculo, que es donde la norma lo
                            pone («círculos numerados»). Con los dos, el paso activo mostraba el
                            número repetido. */}
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

              {/* Derecha — los pasos del trámite + el paso activo.
                  La secuencia y sus nombres NO se eligen aquí: son los que fija la norma FLIT y que
                  la propuesta dibuja igual (ver PASOS_POR_MODALIDAD). Se navega por ellos como
                  `tablist`, con su número delante, y NO se marca "completado" paso a paso: la
                  propuesta lo hace con un `row.prog` inventado, y en un trámite ya radicado —el
                  único que abre este modal— los pasos de captura están cumplidos por construcción.
                  Cada paso pide sus propios datos AL MONTARSE, así que abrir el modal no dispara
                  las siete llamadas de golpe: solo las del paso que se está mirando. */}
              <div className="flex min-w-0 flex-col gap-4 lg:col-span-2">
                {/* Encabezado del paso activo, como en la propuesta: dice en cuál estás sin tener
                    que volver la vista al stepper. */}
                <div className="flex items-center gap-2">
                  <ShieldCheck className="h-4 w-4 shrink-0" style={{ color: BLUE }} aria-hidden="true" />
                  <h3 className="text-sm font-bold" style={{ color: BLUE }}>
                    {pasoActivoIndex + 1}. {pasos[pasoActivoIndex]?.label}
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
                    {/* El paso se llama «Actores y VALIDACIÓN»: el estado de la validación de
                        identidad es parte de él, no un apéndice de la trazabilidad. */}
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

                {seccion === 'expediente' ? (
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
