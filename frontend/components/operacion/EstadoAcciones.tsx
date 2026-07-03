'use client';

import { useEffect, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { estadoChipStyle, estadoLabel } from '@/lib/tramites/estados';

/**
 * N 03 — acciones de transición de estado del trámite en el detalle. El backend manda:
 * solo se pintan botones para los destinos que devuelve `allowedTransitions` del wizard
 * (la máquina de estados); los gates de cada transición los valida el POST /transition.
 *
 * Política de UI: `anulado` → "Anular trámite" (destructivo, motivo OBLIGATORIO);
 * `borrador` (desde rechazado) → "Volver a borrador" (motivo opcional). `preparado`/
 * `entregado` no tienen botón propio (flujo radicar del wizard) y `aprobado`/`rechazado`
 * son decisión del Organismo de Tránsito.
 */

interface AccionConfig {
  toStatus: string;
  label: string;
  destructive: boolean;
  motivoRequerido: boolean;
}

const ACCIONES: AccionConfig[] = [
  { toStatus: 'anulado', label: 'Anular trámite', destructive: true, motivoRequerido: true },
  { toStatus: 'borrador', label: 'Volver a borrador', destructive: false, motivoRequerido: false },
];

export function EstadoAcciones({
  instanceId,
  onChanged,
}: {
  instanceId: string;
  onChanged?: () => void;
}) {
  const [status, setStatus] = useState<string | null>(null);
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
    return () => {
      active = false;
    };
  }, [instanceId]);

  if (!status) return null;

  const acciones = ACCIONES.filter((a) => allowed.includes(a.toStatus));
  const chip = estadoChipStyle(status);

  const ejecutar = async (accion: AccionConfig) => {
    const reason = motivo.trim();
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
        {acciones.map((a) => (
          <button
            key={a.toStatus}
            type="button"
            disabled={working}
            onClick={() => {
              setPending((prev) => (prev?.toStatus === a.toStatus ? null : a));
              setMotivo('');
              setError(null);
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
