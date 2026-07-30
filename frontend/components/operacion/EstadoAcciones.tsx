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
 * Política de UI: `anulado` → "Anular trámite" (destructivo, motivo OBLIGATORIO);
 * Subsanar solo en `rechazado` y sin flag activo → POST /subsanar (activa edición sin
 * cambiar el status). Al terminar, el wizard re-radica directo a `entregado`.
 */

interface AccionConfig {
  toStatus: string;
  label: string;
  destructive: boolean;
  motivoRequerido: boolean;
  /** Ejecuta la acción directo, sin abrir el panel de motivo (p. ej. "Subsanar"). */
  directo?: boolean;
  /** Motivo que viaja por debajo cuando la acción es `directo` (o si el operador no escribe uno). */
  motivoPorDefecto?: string;
  /** Acción especial: activar flag de subsanación (no es transición de estado). */
  subsanar?: boolean;
}

const ACCIONES: AccionConfig[] = [
  { toStatus: 'anulado', label: 'Anular trámite', destructive: true, motivoRequerido: true },
];

const ACCION_SUBSANAR: AccionConfig = {
  toStatus: 'rechazado',
  label: 'Subsanar',
  destructive: false,
  motivoRequerido: false,
  directo: true,
  subsanar: true,
};

export function EstadoAcciones({
  instanceId,
  onChanged,
}: {
  instanceId: string;
  onChanged?: () => void;
}) {
  const [status, setStatus] = useState<string | null>(null);
  const [subsanacionActiva, setSubsanacionActiva] = useState(false);
  // Feature #10587 / HU #10785 — sub-estado interno de placa (ortogonal al status; el trámite sigue
  // en 'entregado'). Gobierna el badge secundario.
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
        setSubsanacionActiva(!!w?.subsanacionActiva);
      })
      .catch(() => {});
    tramitesClient
      .getInstance(instanceId)
      .then((d) => {
        if (!active) return;
        setPlateFlowStatus(d?.plateFlowStatus ?? null);
        if (d?.subsanacionActiva != null) setSubsanacionActiva(!!d.subsanacionActiva);
        if (d?.status) setStatus(d.status);
      })
      .catch(() => {});
    return () => {
      active = false;
    };
  }, [instanceId]);

  if (!status) return null;

  const acciones = [
    ...ACCIONES.filter((a) => allowed.includes(a.toStatus)),
    // Subsanar SOLO en rechazado y mientras el flag no esté activo.
    ...(status === 'rechazado' && !subsanacionActiva ? [ACCION_SUBSANAR] : []),
  ];
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
      if (accion.subsanar) {
        await tramitesClient.startSubsanacion(instanceId);
        setSubsanacionActiva(true);
      } else {
        await tramitesClient.transitionInstance(instanceId, accion.toStatus, reason || undefined);
      }
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
        {subsanacionActiva ? (
          <span
            title="Subsanación activa: el trámite permanece en Rechazado mientras se corrige"
            style={{
              background: 'rgba(245,158,11,0.12)',
              color: '#b45309',
              border: '1px solid rgba(245,158,11,0.3)',
              borderRadius: 999,
              padding: '2px 10px',
              fontSize: 12,
              fontWeight: 600,
            }}
          >
            En subsanación
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
