'use client';

import { useEffect, useRef, useState } from 'react';
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
  // HU #10611 (Feature #10587) — registro del SOAT en la ruta de placa (solo estado 'asignado').
  const [soatEstado, setSoatEstado] = useState<string | null>(null);
  const [soatWorking, setSoatWorking] = useState(false);
  const [soatMsg, setSoatMsg] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement | null>(null);

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
    // Lee el soat_estado persistido para reflejar el registro previo (no solo estado local).
    tramitesClient
      .getInstance(instanceId)
      .then((d) => {
        if (!active) return;
        setSoatEstado(d?.fieldValues?.find((f) => f.fieldKey === 'soat_estado')?.valueText ?? null);
      })
      .catch(() => {});
    return () => {
      active = false;
    };
  }, [instanceId]);

  if (!status) return null;

  const acciones = ACCIONES.filter((a) => allowed.includes(a.toStatus));
  const chip = estadoChipStyle(status);

  // Opción 1 — re-consulta el RUNT del vehículo; si el SOAT viene vigente, el backend marca
  // soat_estado=vigente y desbloquea la aprobación del OT (sin cambiar de estado el trámite).
  const validarSoatRunt = async () => {
    setSoatWorking(true);
    setError(null);
    setSoatMsg(null);
    try {
      const r = await tramitesClient.validateSoatViaRunt(instanceId);
      setSoatEstado(r.soatEstado);
      setSoatMsg(r.message);
      onChanged?.();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'No se pudo validar el SOAT por RUNT.');
    } finally {
      setSoatWorking(false);
    }
  };

  // Opción 2 — carga el PDF del SOAT (permitido en 'asignado') y lo registra como evidencia vigente.
  const subirSoatPdf = async (file: File) => {
    setSoatWorking(true);
    setError(null);
    setSoatMsg(null);
    try {
      await tramitesClient.uploadAttachment(instanceId, 'soat', file);
      await tramitesClient.patchFieldValues(instanceId, [
        { formFieldId: null, fieldKey: 'soat_estado', valueText: 'vigente' },
      ]);
      setSoatEstado('vigente');
      setSoatMsg('SOAT cargado (PDF). El trámite queda listo para la recepción y aprobación del OT.');
      onChanged?.();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'No se pudo cargar el PDF del SOAT.');
    } finally {
      setSoatWorking(false);
    }
  };

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

      {status === 'asignado' ? (
        <div className="flex flex-col gap-3 rounded-xl border p-4">
          <div>
            <p className="m-0 text-sm font-semibold text-slate-700">
              SOAT del vehículo (requerido para que el OT reciba y apruebe)
            </p>
            <p className="m-0 mt-1 text-xs text-slate-500">
              Valida el SOAT por consulta RUNT o carga el PDF. Sin un SOAT vigente el OT no puede
              aprobar la matrícula (gate no subsanable).
            </p>
          </div>

          <div className="flex items-center gap-2 text-xs">
            <span className="font-semibold uppercase tracking-wide opacity-60">SOAT:</span>
            <span
              className={`rounded-full px-2 py-0.5 text-[11px] font-semibold ${
                soatEstado === 'vigente'
                  ? 'bg-green-100 text-green-700'
                  : soatEstado === 'vencido'
                    ? 'bg-orange-100 text-orange-700'
                    : 'bg-slate-100 text-slate-600'
              }`}
            >
              {soatEstado === 'vigente'
                ? 'Vigente'
                : soatEstado === 'vencido'
                  ? 'Vencido'
                  : soatEstado === 'unknown'
                    ? 'No reportado'
                    : 'Sin registrar'}
            </span>
          </div>

          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              disabled={soatWorking}
              onClick={() => void validarSoatRunt()}
              className="rounded-lg bg-[#557eff] px-3.5 py-1.5 text-xs font-semibold text-white disabled:opacity-60"
            >
              {soatWorking ? 'Validando…' : 'Validar por RUNT'}
            </button>
            <button
              type="button"
              disabled={soatWorking}
              onClick={() => fileInputRef.current?.click()}
              className="rounded-lg border border-slate-300 px-3.5 py-1.5 text-xs font-semibold text-slate-600 disabled:opacity-60"
            >
              Cargar PDF del SOAT
            </button>
            <input
              ref={fileInputRef}
              type="file"
              accept="application/pdf,image/*"
              className="hidden"
              onChange={(e) => {
                const f = e.target.files?.[0];
                e.target.value = '';
                if (f) void subirSoatPdf(f);
              }}
            />
          </div>

          {soatMsg ? (
            <p
              className={`m-0 text-xs ${
                soatEstado === 'vigente'
                  ? 'text-green-700'
                  : soatEstado === 'vencido'
                    ? 'text-orange-700'
                    : 'text-slate-500'
              }`}
              role={soatEstado === 'vigente' ? undefined : 'alert'}
            >
              {soatMsg}
            </p>
          ) : soatEstado === 'vigente' ? (
            <p className="m-0 text-xs text-green-700">
              SOAT registrado como vigente. El OT ya puede recibir y aprobar la matrícula.
            </p>
          ) : soatEstado === 'vencido' ? (
            <p role="alert" className="m-0 text-xs text-orange-700">
              SOAT vencido: el OT no podrá aprobar hasta que esté vigente (gate no subsanable).
            </p>
          ) : (
            <p className="m-0 text-xs text-slate-500">
              SOAT sin registrar. Valida por RUNT o carga el PDF; sin SOAT el OT no puede aprobar.
            </p>
          )}
        </div>
      ) : null}

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
