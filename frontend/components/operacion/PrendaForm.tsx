'use client';

import { forwardRef, useEffect, useImperativeHandle, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { digitsOnly } from '@/lib/format/currency';
import { useWizardReadOnly } from './WizardReadOnlyContext';
import type { WizardStepFormHandle } from './wizard-step-form';
import type { PrendaDecision } from '@/lib/api/types/procedure-runtime';

/** Handle imperativo: la shell del wizard dispara guardar+validar. */
export type PrendaFormHandle = WizardStepFormHandle;

/** Etiquetas legibles de cada decisión de prenda (contrato con el backend). */
export const PRENDA_DECISION_LABELS: Record<PrendaDecision, string> = {
  solicitar: 'Solicitar constitución de prenda',
  registrar: 'Registrar prenda existente',
  levantar: 'Levantar gravamen',
  omitir: 'Continuar sin gestionar (asumo el riesgo)',
  sin_prenda: 'Sin prenda',
};

/** Decisiones que exigen el documento de soporte (se carga en el paso de documentos). */
const REQUIERE_DOCUMENTO: ReadonlySet<PrendaDecision> = new Set<PrendaDecision>([
  'solicitar',
  'registrar',
  'levantar',
]);

/** Decisiones que capturan datos del acreedor (beneficiario del gravamen) para el FUR. */
const CAPTURA_ACREEDOR: ReadonlySet<PrendaDecision> = new Set<PrendaDecision>([
  'solicitar',
  'registrar',
]);

/** En matrícula la prenda es declarativa: registrar o sin prenda. */
const MATRICULA_DECISIONS: PrendaDecision[] = ['registrar', 'sin_prenda'];

interface Props {
  instanceId: string | null;
  /** Decisiones ofrecidas (default: matrícula declarativa). Traspaso pasa las 4 de gestión. */
  decisions?: PrendaDecision[];
  /** Se invoca tras un guardado exitoso (la shell refresca el wizard). */
  onSaved?: () => void;
  hideHeader?: boolean;
  embeddedInWizard?: boolean;
}

const INPUT_BASE =
  'w-full px-3 py-2 rounded-xl border bg-white dark:bg-[#0B0F14] text-xs outline-none focus:border-[#557EFF] aria-[invalid=true]:border-[#FF4E00]';

/**
 * Captura de la decisión de prenda (gravamen) del trámite. En matrícula (R4) es declarativa e
 * informativa (no bloquea la radicación); en traspaso (R10) el gate lo aplica el backend. El
 * documento de soporte se carga en el paso de documentos; aquí solo se registra la decisión y,
 * cuando aplica, los datos del acreedor que se reflejarán en el FUR.
 */
export const PrendaForm = forwardRef<PrendaFormHandle, Props>(function PrendaForm(
  {
    instanceId,
    decisions = MATRICULA_DECISIONS,
    onSaved,
    hideHeader = false,
    embeddedInWizard = false,
  },
  ref,
) {
  const readOnly = useWizardReadOnly();
  const [decision, setDecision] = useState<PrendaDecision | ''>('');
  const [acreedorNombre, setAcreedorNombre] = useState('');
  const [acreedorDocumento, setAcreedorDocumento] = useState('');
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!instanceId) return;
    let active = true;
    const load = async () => {
      setLoading(true);
      try {
        const p = await tramitesClient.getPrenda(instanceId);
        if (active && p) {
          setDecision(p.decision);
          setAcreedorNombre(p.acreedorNombre ?? '');
          setAcreedorDocumento(digitsOnly(p.acreedorDocumento ?? ''));
        }
      } catch {
        /* sin decisión previa: el form queda vacío */
      } finally {
        if (active) setLoading(false);
      }
    };
    void load();
    return () => {
      active = false;
    };
  }, [instanceId]);

  const capturaAcreedor = decision !== '' && CAPTURA_ACREEDOR.has(decision);
  const requiereDocumento = decision !== '' && REQUIERE_DOCUMENTO.has(decision);

  // Persiste la decisión seleccionada. En matrícula es informativa: si no se elige nada, no bloquea
  // (save() devuelve true sin escribir). Devuelve true si el paso puede continuar.
  const submit = async (): Promise<boolean> => {
    if (!instanceId) return false;
    if (decision === '') return true; // declarativa: sin decisión no bloquea
    setSaving(true);
    setSaved(false);
    setError(null);
    try {
      await tramitesClient.putPrenda(instanceId, {
        decision,
        acreedorNombre: capturaAcreedor ? acreedorNombre.trim() || null : null,
        acreedorDocumento: capturaAcreedor ? acreedorDocumento.trim() || null : null,
      });
      setSaved(true);
      onSaved?.();
      return true;
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Error al guardar la decisión de prenda');
      return false;
    } finally {
      setSaving(false);
    }
  };

  useImperativeHandle(ref, () => ({ save: submit }));

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    await submit();
  };

  return (
    <form
      onSubmit={handleSubmit}
      className="rounded-2xl p-4 border bg-white dark:bg-[#0B0F14]"
      aria-label="Prenda o gravamen del trámite"
      noValidate
    >
      {!hideHeader && (
        <div className="mb-3">
          <h4 className="text-sm font-bold">Prenda / gravamen</h4>
          <p className="text-[11px] opacity-60">
            Declara si el vehículo tiene prenda. El documento de soporte se carga en el paso de
            documentos.
          </p>
        </div>
      )}

      {error && (
        <div
          className="rounded-xl p-3 text-xs border mb-3 flex items-center justify-between gap-3"
          style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          <span>{error}</span>
          <button type="button" onClick={() => setError(null)} className="font-bold" aria-label="Descartar error">
            ×
          </button>
        </div>
      )}

      <fieldset disabled={readOnly} className="contents">
        <div className="grid grid-cols-1 gap-4">
          <div>
            <label htmlFor="prenda-decision" className="text-xs font-semibold mb-1.5 block">
              Decisión de prenda
            </label>
            <select
              id="prenda-decision"
              value={decision}
              onChange={(e) => setDecision((e.target.value || '') as PrendaDecision | '')}
              className={INPUT_BASE}
            >
              <option value="">Selecciona una opción…</option>
              {decisions.map((d) => (
                <option key={d} value={d}>
                  {PRENDA_DECISION_LABELS[d]}
                </option>
              ))}
            </select>
          </div>

          {capturaAcreedor && (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div>
                <label htmlFor="prenda-acreedor-nombre" className="text-xs font-semibold mb-1.5 block">
                  Acreedor (beneficiario)
                </label>
                <input
                  id="prenda-acreedor-nombre"
                  type="text"
                  value={acreedorNombre}
                  onChange={(e) => setAcreedorNombre(e.target.value)}
                  placeholder="Ej. Banco XYZ"
                  className={INPUT_BASE}
                />
              </div>
              <div>
                <label htmlFor="prenda-acreedor-doc" className="text-xs font-semibold mb-1.5 block">
                  NIT / documento del acreedor
                </label>
                <input
                  id="prenda-acreedor-doc"
                  type="text"
                  inputMode="numeric"
                  pattern="[0-9]*"
                  autoComplete="off"
                  value={acreedorDocumento}
                  onChange={(e) => setAcreedorDocumento(digitsOnly(e.target.value))}
                  className={INPUT_BASE}
                />
              </div>
            </div>
          )}

          {requiereDocumento && (
            <p className="text-[11px] opacity-60" role="note">
              Recuerda cargar el documento de prenda en el paso de documentos.
            </p>
          )}
        </div>
      </fieldset>

      {!readOnly && !embeddedInWizard && (
        <div className="flex items-center justify-between gap-3 mt-4">
          {saved ? (
            <span className="text-[11px] font-semibold" style={{ color: '#8CC63F' }} role="status" aria-live="polite">
              Decisión de prenda guardada ✓
            </span>
          ) : (
            <span className="text-[11px] opacity-50">{loading ? 'Cargando…' : ''}</span>
          )}
          <button
            type="submit"
            disabled={saving}
            className="px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
            style={{ background: '#557EFF' }}
          >
            {saving ? 'Guardando…' : 'Guardar decisión de prenda'}
          </button>
        </div>
      )}
    </form>
  );
});
