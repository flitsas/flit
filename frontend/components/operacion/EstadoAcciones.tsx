'use client';

import { useEffect, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import {
  estadoChipStyle,
  estadoLabel,
  plateFlowChipStyle,
  plateFlowLabel,
} from '@/lib/tramites/estados';
import type { PlateFlowStatus } from '@/lib/api/types/procedure-runtime';

/**
 * N 03 — acciones de transición de estado del trámite en el detalle. El backend manda:
 * solo se pintan botones para los destinos que devuelve `allowedTransitions` del wizard
 * (la máquina de estados); los gates de cada transición los valida el POST /transition.
 *
 * Política de UI: `anulado` → "Anular trámite" (destructivo, motivo OBLIGATORIO);
 * `subsanacion` (desde rechazado) → "Subsanar" (HU #10870/#10872): reabre la edición COMPLETA
 * en sitio (como borrador) SIN devolver el trámite a `borrador`; al terminar, el wizard lo
 * re-radica directo a `entregado`. `preparado`/`entregado` no tienen botón propio (flujo
 * radicar del wizard) y `aprobado`/`rechazado` son decisión del Organismo de Tránsito.
 */

interface AccionConfig {
  toStatus: string;
  label: string;
  destructive: boolean;
  motivoRequerido: boolean;
  /** Ejecuta la transición directo, sin abrir el panel de motivo (p. ej. "Subsanar"). */
  directo?: boolean;
  /** Motivo que viaja por debajo cuando la acción es `directo` (o si el operador no escribe uno). */
  motivoPorDefecto?: string;
}

const ACCIONES: AccionConfig[] = [
  { toStatus: 'anulado', label: 'Anular trámite', destructive: true, motivoRequerido: true },
  // HU #10870/#10872 — subsanación por el operador: reabre la edición en sitio (como borrador)
  // sin pasar por `borrador`; el wizard cierra re-radicando directo a `entregado`. Es una acción
  // DIRECTA: no pide motivo al operador (solo aplica a subsanación) y envía un motivo por defecto.
  {
    toStatus: 'subsanacion',
    label: 'Subsanar',
    destructive: false,
    motivoRequerido: false,
    directo: true,
    motivoPorDefecto: 'Subsanación iniciada por el operador',
  },
];

export function EstadoAcciones({
  instanceId,
  onChanged,
}: {
  instanceId: string;
  onChanged?: () => void;
}) {
  const [status, setStatus] = useState<string | null>(null);
  // Feature #10587 / HU #10785 — sub-estado interno de placa (ortogonal al status; el trámite sigue
  // en 'entregado'). Gobierna el badge secundario y el panel de SOAT.
  const [plateFlowStatus, setPlateFlowStatus] = useState<PlateFlowStatus | null>(null);
  const [allowed, setAllowed] = useState<string[]>([]);
  const [pending, setPending] = useState<AccionConfig | null>(null);
  const [motivo, setMotivo] = useState('');
  const [working, setWorking] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    tramitesClient
      .getWizardState(instanceId)
      .then((w) => {
        if (!active) return;
        setStatus(w?.status ?? null);
        setAllowed(w?.allowedTransitions ?? []);
      })
      .catch(() => {});
    // Lee el sub-estado de placa para pintar el badge secundario (el trámite sigue en 'entregado').
    // El registro del SOAT vive ahora en el paso FUR (SoatSection), no en este panel.
    tramitesClient
      .getInstance(instanceId)
      .then((d) => {
        if (!active) return;
        setPlateFlowStatus(d?.plateFlowStatus ?? null);
      })
      .catch(() => {});
    return () => {
      active = false;
    };
  }, [instanceId]);

  if (!status) return null;

  const acciones = ACCIONES.filter((a) => allowed.includes(a.toStatus));
  const chip = estadoChipStyle(status);

  const ejecutar = async (accion: AccionConfig) => {
    const reason = motivo.trim() || accion.motivoPorDefecto?.trim() || '';
    if (accion.motivoRequerido && !reason) {
      setError('Debes indicar el motivo para esta transición.');
      return;
    }
    setWorking(true);
    setError(null);
    try {
      await tramitesClient.transitionInstance(instanceId, accion.toStatus, reason || undefined);
      setPending(null);
      setMotivo('');
      onChanged?.();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'No se pudo cambiar el estado del trámite.');
    } finally {
      setWorking(false);
    }
  };

  return (
    <section
      style={{
        margin: '16px auto 0',
        maxWidth: 960,
        padding: '0 16px',
        display: 'flex',
        flexDirection: 'column',
        gap: 10,
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
        <span style={{ color: '#475569', fontSize: 13, fontWeight: 600 }}>Estado del trámite:</span>
        <span
          style={{
            background: chip.bg,
            color: chip.color,
            border: `1px solid ${chip.border}`,
            borderRadius: 999,
            padding: '2px 10px',
            fontSize: 12,
            fontWeight: 600,
          }}
        >
          {estadoLabel(status)}
        </span>
        {plateFlowChipStyle(plateFlowStatus) ? (
          <span
            title="Progreso de la placa (sub-estado interno; el trámite sigue en Entregado)"
            style={{
              background: plateFlowChipStyle(plateFlowStatus)!.bg,
              color: plateFlowChipStyle(plateFlowStatus)!.color,
              border: `1px solid ${plateFlowChipStyle(plateFlowStatus)!.border}`,
              borderRadius: 999,
              padding: '2px 10px',
              fontSize: 12,
              fontWeight: 600,
            }}
          >
            {plateFlowLabel(plateFlowStatus)}
          </span>
        ) : null}
        {acciones.map((a) => (
          <button
            key={a.toStatus}
            type="button"
            disabled={working}
            onClick={() => {
              setMotivo('');
              setError(null);
              // Acción directa (Subsanar): transiciona de inmediato con el motivo por defecto,
              // sin abrir el panel de motivo.
              if (a.directo) {
                setPending(null);
                void ejecutar(a);
                return;
              }
              setPending((prev) => (prev?.toStatus === a.toStatus ? null : a));
            }}
            style={{
              background: 'transparent',
              border: `1px solid ${a.destructive ? '#fca5a5' : '#cbd5e1'}`,
              color: a.destructive ? '#b91c1c' : '#475569',
              borderRadius: 8,
              padding: '4px 12px',
              fontSize: 12,
              fontWeight: 600,
              cursor: 'pointer',
            }}
          >
            {a.label}
          </button>
        ))}
      </div>

      {pending ? (
        <div
          style={{
            background: '#fff',
            border: '1px solid #e2e8f0',
            borderRadius: 12,
            padding: 12,
            display: 'flex',
            flexDirection: 'column',
            gap: 8,
          }}
        >
          <label style={{ color: '#334155', fontSize: 12, fontWeight: 600 }}>
            Motivo{pending.motivoRequerido ? ' (obligatorio)' : ' (opcional)'}
            <textarea
              value={motivo}
              onChange={(e) => setMotivo(e.target.value)}
              rows={2}
              placeholder={
                pending.motivoRequerido
                  ? 'Indica por qué se anula el trámite…'
                  : 'Motivo del cambio de estado…'
              }
              style={{
                display: 'block',
                width: '100%',
                marginTop: 6,
                border: '1px solid #cbd5e1',
                borderRadius: 8,
                padding: 8,
                fontSize: 13,
                resize: 'vertical',
              }}
            />
          </label>
          <div style={{ display: 'flex', gap: 8 }}>
            <button
              type="button"
              disabled={working}
              onClick={() => void ejecutar(pending)}
              style={{
                background: pending.destructive ? '#dc2626' : '#557eff',
                border: 'none',
                color: '#fff',
                borderRadius: 8,
                padding: '6px 14px',
                fontSize: 12,
                fontWeight: 600,
                cursor: 'pointer',
              }}
            >
              {working ? 'Aplicando…' : `Confirmar: ${pending.label}`}
            </button>
            <button
              type="button"
              disabled={working}
              onClick={() => {
                setPending(null);
                setError(null);
              }}
              style={{
                background: 'transparent',
                border: '1px solid #cbd5e1',
                color: '#475569',
                borderRadius: 8,
                padding: '6px 14px',
                fontSize: 12,
                cursor: 'pointer',
              }}
            >
              Cancelar
            </button>
          </div>
        </div>
      ) : null}

      {error ? (
        <p role="alert" style={{ color: '#c2410c', fontSize: 13, margin: 0 }}>
          {error}
        </p>
      ) : null}
    </section>
  );
}
