'use client';

import { useCallback, useEffect, useState } from 'react';
import { Check, RefreshCw, ShieldCheck } from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import type {
  BiometricParte,
  BiometricValidation,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

interface Props {
  instanceId: string | null;
  modalidad: WizardModalidad;
  /** Re-consulta el estado del wizard tras simular/refrescar (server-driven). */
  onRefresh?: () => void;
  /**
   * Oculta el párrafo introductorio cuando el contenedor ya describe el paso
   * (paso `identidad`: el h2 + subtítulo del wizard lo cubren). En `fur` NO se
   * oculta: ahí la biométrica es una subsección dentro de "Generar FUR".
   */
  hideIntro?: boolean;
}

/** Partes que requieren biométrica por modalidad. */
function partesFor(modalidad: WizardModalidad): BiometricParte[] {
  return modalidad === 'traspaso' ? ['comprador', 'vendedor'] : ['comprador'];
}

const PARTE_LABEL: Record<BiometricParte, string> = {
  comprador: 'Comprador',
  vendedor: 'Vendedor',
};

/**
 * Paso de validación de identidad. En esta iteración la biométrica real está
 * mockeada: por cada parte requerida (matrícula → comprador; traspaso →
 * comprador + vendedor) se ofrece un botón "Simular validación de identidad"
 * que aprueba la validación (score 95). Al aprobarse, la tarjeta se pinta en
 * verde con "Identidad verificada — {score}/100". El status/gating lo decide el
 * wizard server-driven: este paso solo refresca tras simular.
 */
export function BiometricStep({ instanceId, modalidad, onRefresh, hideIntro = false }: Props) {
  const partes = partesFor(modalidad);

  const [validations, setValidations] = useState<BiometricValidation[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!instanceId) return;
    try {
      const list = await tramitesClient.listBiometric(instanceId);
      setValidations(list);
      setError(() => null);
    } catch (err) {
      setError(() =>
        err instanceof Error ? err.message : 'Error al cargar las validaciones.',
      );
    }
  }, [instanceId]);

  useEffect(() => {
    void load();
  }, [load]);

  const handleRefresh = async () => {
    setLoading(true);
    try {
      await load();
    } finally {
      setLoading(false);
    }
    onRefresh?.();
  };

  return (
    <div className="space-y-4">
      <div className="flex items-start justify-between gap-3">
        {hideIntro ? (
          <span />
        ) : (
          <p className="text-xs opacity-70">
            Validación de identidad de cada parte. La biométrica real llegará en una
            iteración futura; por ahora puedes simular la validación de cada parte.
          </p>
        )}
        <button
          type="button"
          onClick={() => void handleRefresh()}
          disabled={loading || !instanceId}
          className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-[11px] font-semibold border shrink-0 disabled:opacity-50"
          style={{ borderColor: '#557EFF', color: '#557EFF' }}
          aria-label="Actualizar estado biométrico"
        >
          <RefreshCw className={`h-3 w-3 ${loading ? 'animate-spin' : ''}`} />
          Actualizar
        </button>
      </div>

      {error && (
        <div
          className="rounded-xl p-3 text-xs border"
          style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          {error}
        </div>
      )}

      <div className="space-y-4">
        {partes.map((parte) => {
          const validation = (validations ?? []).find((v) =>
            modalidad === 'traspaso'
              ? v.parte === parte
              : v.parte === null || v.parte === 'comprador',
          );
          const approved = validation?.estado === 'aprobado';
          return (
            <ParteCard
              key={parte}
              parte={parte}
              instanceId={instanceId}
              validation={approved ? validation ?? null : null}
              onChanged={() => void handleRefresh()}
            />
          );
        })}
      </div>
    </div>
  );
}

/** Tarjeta por parte: resultado verificado (verde) o botón para simular. */
function ParteCard({
  parte,
  instanceId,
  validation,
  onChanged,
}: {
  parte: BiometricParte;
  instanceId: string | null;
  validation: BiometricValidation | null;
  onChanged: () => void;
}) {
  return (
    <fieldset
      className="rounded-xl border p-4"
      style={{ borderColor: '#DFE5ED' }}
      aria-label={`Biométrica ${PARTE_LABEL[parte]}`}
    >
      <legend className="px-1 text-xs font-bold">{PARTE_LABEL[parte]}</legend>

      {validation ? (
        <VerifiedView validation={validation} />
      ) : (
        <SimulateAction parte={parte} instanceId={instanceId} onSimulated={onChanged} />
      )}
    </fieldset>
  );
}

/** Tarjeta verde "Identidad verificada — {score}/100" con el nombre de la parte. */
function VerifiedView({ validation: v }: { validation: BiometricValidation }) {
  return (
    <div
      className="flex items-center gap-3 rounded-xl p-3"
      style={{ background: 'rgba(140,198,63,0.12)', border: '1px solid rgba(140,198,63,0.4)' }}
    >
      <span
        className="flex h-9 w-9 items-center justify-center rounded-full shrink-0"
        style={{ background: '#5B8A1F', color: 'white' }}
        aria-hidden
      >
        <Check className="h-5 w-5" />
      </span>
      <div className="space-y-0.5">
        <p className="text-xs font-bold" style={{ color: '#5B8A1F' }}>
          Identidad verificada — {v.score ?? 95}/100
        </p>
        <p className="text-[11px] opacity-70">{v.nombre}</p>
      </div>
    </div>
  );
}

/** Acción de simular la validación de identidad de una parte (mock). */
function SimulateAction({
  parte,
  instanceId,
  onSimulated,
}: {
  parte: BiometricParte;
  instanceId: string | null;
  onSimulated: () => void;
}) {
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSimulate = async () => {
    if (!instanceId) return;
    setError(null);
    setSubmitting(true);
    try {
      await tramitesClient.simulateBiometric(instanceId, { parte });
      onSimulated();
    } catch (err) {
      setError(
        err instanceof Error ? err.message : 'No se pudo simular la validación.',
      );
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="space-y-3">
      <p className="text-[11px] opacity-60">
        Mock de esta iteración: simula la validación biométrica de esta parte. La
        captura biométrica real se integrará más adelante.
      </p>

      {error && (
        <p className="text-[11px] font-medium" style={{ color: '#FF4E00' }} role="alert">
          {error}
        </p>
      )}

      <button
        type="button"
        onClick={() => void handleSimulate()}
        disabled={submitting || !instanceId}
        className="flex items-center gap-2 px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
        style={{ background: '#557EFF' }}
      >
        {submitting ? (
          <RefreshCw className="h-3.5 w-3.5 animate-spin" />
        ) : (
          <ShieldCheck className="h-3.5 w-3.5" />
        )}
        {submitting ? 'Simulando…' : 'Simular validación de identidad'}
      </button>
    </div>
  );
}
