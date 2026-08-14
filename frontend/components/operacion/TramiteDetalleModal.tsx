'use client';

import { useEffect, useState } from 'react';
import { Clock, Download } from 'lucide-react';
import { Modal } from '@/components/atom/Modal';
import { InlineAlert } from '@/components/atom/InlineAlert';
import { StatusBadge } from '@/components/atom/StatusBadge';
import ExpedienteTimeline from './ExpedienteTimeline';
import { plateFlowHint } from './TramitesTable';
import { AttachmentPreview, useAttachmentPreview } from './TramiteDocumentosModal';
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

/** Skeleton de 3 barras — mismo patrón que el resto del módulo (`TramiteDocumentosModal`). */
function Skeleton({ count, label }: { count: number; label: string }) {
  return (
    <div className="space-y-2" role="status" aria-busy="true" aria-label={label}>
      {Array.from({ length: count }).map((_, i) => (
        // Mismo gris de esqueleto que usa el listado: el borde de marca al 50%. `#F4F7FC` no sale
        // ni de la propuesta ni de globals.css.
        <div
          key={i}
          className="h-10 animate-pulse rounded-xl dark:bg-white/5"
          style={{ background: 'rgba(223,229,237,0.5)' }}
        />
      ))}
    </div>
  );
}

function ErrorRetry({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <div className="flex flex-col items-start gap-2 py-1" role="alert">
      <p className="text-xs" style={{ color: '#C2410C' }}>
        {message}
      </p>
      <button
        type="button"
        onClick={onRetry}
        className="rounded-full border px-4 py-1.5 text-xs font-semibold transition hover:bg-[#557EFF]/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
        style={{ borderColor: BLUE, color: BLUE }}
      >
        Reintentar
      </button>
    </div>
  );
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

              {/* Derecha — Trazabilidad: ExpedienteTimeline (statusHistory de getInstance) + archivos
                  finales generados por el sistema (getAttachments, filtrado a source === 'system'). */}
              <div className="flex flex-col gap-4 lg:col-span-2">
                <div className="flex items-center gap-2">
                  <Clock className="h-4 w-4" style={{ color: BLUE }} aria-hidden="true" />
                  <h3 className="text-sm font-bold" style={{ color: BLUE }}>
                    Trazabilidad
                  </h3>
                </div>

                {loading ? <Skeleton count={3} label="Cargando trazabilidad" /> : null}
                {!loading && error ? (
                  <ErrorRetry message={error} onRetry={() => setDetailReloadKey((k) => k + 1)} />
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
                  {attLoading ? <Skeleton count={2} label="Cargando archivos finales" /> : null}
                  {!attLoading && attError ? (
                    <ErrorRetry message={attError} onRetry={() => setAttReloadKey((k) => k + 1)} />
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
              </div>
            </div>
          </div>
        )}
      </Modal>

      <AttachmentPreview preview={preview} />
    </>
  );
}
