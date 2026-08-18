'use client';

import { useCallback, useEffect, useState } from 'react';
import { Check, Copy, RefreshCw } from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { Signature, SignatureParte } from '@/lib/api/types/procedure-runtime';
import { useWizardReadOnly } from './WizardReadOnlyContext';
import { WIZARD_INPUT, WIZARD_CTA_GRADIENT } from './wizard-field-styles';

const PARTE_LABEL: Record<SignatureParte, string> = {
  comprador: 'Comprador',
  vendedor: 'Vendedor',
};

const INPUT_BASE = WIZARD_INPUT;

function FirmaBadge({ estado }: { estado: string }) {
  const map: Record<string, { label: string; bg: string; color: string }> = {
    pendiente_envio: { label: 'Pendiente', bg: '#EEF1F5', color: '#59677D' },
    enviada: { label: 'Enviada', bg: 'rgba(85,126,255,0.12)', color: '#557EFF' },
    firmada: { label: 'Firmada', bg: 'rgba(140,198,63,0.15)', color: 'var(--flit-success-ink)' },
    rechazada: { label: 'Rechazada', bg: 'rgba(255,78,0,0.10)', color: '#FF4E00' },
  };
  const s = map[estado] ?? { label: estado, bg: '#EEF1F5', color: '#59677D' };
  return (
    <span
      className="px-2.5 py-1 rounded-full text-xs font-bold"
      style={{ background: s.bg, color: s.color }}
    >
      {s.label}
    </span>
  );
}

function CopyLink({ link, label }: { link: string; label: string }) {
  const [copied, setCopied] = useState(false);
  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(link);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      /* enlace visible para copiar a mano */
    }
  };
  return (
    <div className="flex items-center gap-2">
      <input type="text" readOnly value={link} aria-label={label} className={INPUT_BASE} />
      <button
        type="button"
        onClick={() => void handleCopy()}
        className="flex items-center gap-1.5 px-3 py-2 rounded-xl text-xs font-semibold text-white shrink-0"
        style={{ background: WIZARD_CTA_GRADIENT }}
        aria-label="Copiar enlace"
      >
        {copied ? <Check className="h-3 w-3" /> : <Copy className="h-3 w-3" />}
        {copied ? 'Copiado' : 'Copiar'}
      </button>
    </div>
  );
}

/**
 * Estado informativo de la firma de compraventa de una parte (traspaso).
 * Se embebe en Comprador/Vendedor del resumen; no bloquea preparar ni radicar.
 */
export function CompraventaFirmaStatus({
  instanceId,
  parte,
  onChanged,
}: {
  instanceId: string;
  parte: SignatureParte;
  onChanged?: () => void;
}) {
  const readOnly = useWizardReadOnly();
  const [signature, setSignature] = useState<Signature | null>(null);
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const list = await tramitesClient.listFirmas(instanceId);
      setSignature(list.find((s) => s.parte === parte) ?? null);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Error al cargar la firma.');
    }
  }, [instanceId, parte]);

  useEffect(() => {
    // Carga async al montar (setState tras await), mismo patrón que FirmaPosteriorSection.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  const refresh = async () => {
    setLoading(true);
    try {
      await load();
    } finally {
      setLoading(false);
    }
    onChanged?.();
  };

  const handleSimular = async () => {
    if (!signature) return;
    setBusy(true);
    setError(null);
    try {
      await tramitesClient.simularFirma(instanceId, signature.id);
      await refresh();
    } catch {
      setError('No se pudo simular la firma.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div
      className="space-y-2 rounded-xl border px-3 py-3"
      style={{ borderColor: '#DFE5ED' }}
      role="group"
      aria-label={`Firma compraventa ${PARTE_LABEL[parte]}`}
    >
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="text-xs font-bold uppercase tracking-[0.2em] opacity-55">
            Firma de la compraventa
          </p>
          <p className="mt-0.5 text-xs opacity-60">
            Informativo · se apalanca de la validación de identidad o la firma del baúl · no bloquea
            el traspaso.
          </p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          {signature ? <FirmaBadge estado={signature.estado} /> : null}
          {!readOnly && !signature?.firmada ? (
            <button
              type="button"
              onClick={() => void refresh()}
              disabled={loading}
              className="flex items-center gap-1 rounded-lg border px-2 py-1 text-xs font-semibold disabled:opacity-50"
              style={{ borderColor: '#557EFF', color: '#557EFF' }}
              aria-label={`Actualizar firma ${PARTE_LABEL[parte]}`}
            >
              <RefreshCw className={`h-3 w-3 ${loading ? 'animate-spin' : ''}`} />
              Actualizar
            </button>
          ) : null}
        </div>
      </div>

      {signature ? (
        <div className="space-y-2 text-xs">
          {signature.signUrl && signature.estado !== 'firmada' ? (
            <CopyLink
              link={signature.signUrl}
              label={`Enlace de firma ${PARTE_LABEL[parte]}`}
            />
          ) : null}
          {signature.estado === 'firmada' ? (
            <p className="flex items-center gap-1.5 font-semibold" style={{ color: 'var(--flit-success-ink)' }}>
              <Check className="h-3.5 w-3.5" /> Compraventa firmada
            </p>
          ) : null}
          {signature.estado === 'enviada' && !readOnly ? (
            <button
              type="button"
              onClick={() => void handleSimular()}
              disabled={busy}
              className="rounded-xl border px-4 py-1.5 text-xs font-semibold disabled:opacity-50"
              style={{ borderColor: '#557EFF', color: '#557EFF' }}
            >
              {busy ? 'Simulando…' : 'Simular firma (DEV)'}
            </button>
          ) : null}
        </div>
      ) : (
        <p className="text-xs opacity-60">Firma no solicitada.</p>
      )}

      {error ? (
        <p className="text-xs font-medium" style={{ color: '#FF4E00' }} role="alert">
          {error}
        </p>
      ) : null}
    </div>
  );
}
