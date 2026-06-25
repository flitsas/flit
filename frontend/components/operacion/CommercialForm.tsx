'use client';

import { forwardRef, useEffect, useImperativeHandle, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { digitsOnly, groupThousands } from '@/lib/format/currency';
import { useWizardReadOnly } from './WizardReadOnlyContext';
import type { WizardStepFormHandle } from './wizard-step-form';
import type {
  CommercialCausal,
  CommercialData,
} from '@/lib/api/types/procedure-runtime';

/** Handle imperativo: la shell del wizard dispara guardar+validar. */
export type CommercialFormHandle = WizardStepFormHandle;

interface Props {
  instanceId: string | null;
  /** Se invoca tras un guardado exitoso (la shell refresca el wizard). */
  onSaved?: () => void;
  /**
   * Oculta el título "Datos comerciales" y su descripción cuando el contenedor
   * ya pinta el título del paso (el wizard lo hace con su h2 + subtítulo).
   */
  hideHeader?: boolean;
  /**
   * Embebido en el wizard: oculta el botón "Guardar datos comerciales" propio
   * (la shell dispara save() vía ref desde el footer "Guardar y continuar").
   */
  embeddedInWizard?: boolean;
}

const CAUSAL_OPTIONS: { value: CommercialCausal; label: string }[] = [
  { value: 'COMPRAVENTA', label: 'Compraventa' },
  { value: 'DONACION', label: 'Donación' },
  { value: 'DACION_EN_PAGO', label: 'Dación en pago' },
  { value: 'ADJUDICACION', label: 'Adjudicación' },
];

const INPUT_BASE =
  'w-full px-3 py-2 rounded-xl border bg-white dark:bg-[#0B0F14] text-xs outline-none focus:border-[#557EFF] aria-[invalid=true]:border-[#FF4E00]';

const EMPTY: CommercialData = {
  valorVenta: null,
  causal: null,
  tasaImpuesto: null,
  derechos: null,
  metodoPago: null,
};

function numberOrNull(v: string): number | null {
  if (v.trim() === '') return null;
  const n = Number(v);
  return Number.isFinite(n) ? n : null;
}

/**
 * Captura de datos comerciales del traspaso (valor de venta, causal, tasa de
 * impuesto, derechos, método de pago). Carga/guarda vía el cliente; el envío
 * exitoso dispara `onSaved` para que la shell re-consulte el wizard.
 */
export const CommercialForm = forwardRef<CommercialFormHandle, Props>(
  function CommercialForm(
    { instanceId, onSaved, hideHeader = false, embeddedInWizard = false },
    ref,
  ) {
  // Solo lectura (Track C): inputs deshabilitados + sin botón guardar.
  const readOnly = useWizardReadOnly();
  const [data, setData] = useState<CommercialData>(EMPTY);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!instanceId) return;
    let active = true;
    // setState dentro de la función async (no en el cuerpo síncrono del
    // effect) para no disparar la regla react-hooks/set-state-in-effect.
    const load = async () => {
      setLoading(true);
      try {
        const d = await tramitesClient.getCommercial(instanceId);
        if (active && d) setData({ ...EMPTY, ...d });
      } catch {
        /* sin datos previos: se queda el form vacío */
      } finally {
        if (active) setLoading(false);
      }
    };
    void load();
    return () => {
      active = false;
    };
  }, [instanceId]);

  const valid =
    data.valorVenta != null && data.valorVenta > 0 && data.causal != null;

  // Valida + persiste. Núcleo compartido por el submit propio y el save() del
  // ref (footer "Guardar y continuar" del wizard). Devuelve true si persistió.
  const submit = async (): Promise<boolean> => {
    if (!instanceId) return false;
    if (!valid) {
      setError('Ingresa el valor de venta y la causal para continuar.');
      return false;
    }
    setSaving(true);
    setSaved(false);
    setError(null);
    try {
      await tramitesClient.putCommercial(instanceId, data);
      setSaved(true);
      onSaved?.();
      return true;
    } catch (err) {
      setError(
        err instanceof Error ? err.message : 'Error al guardar los datos comerciales',
      );
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
      style={{ borderColor: '#DFE5ED' }}
      aria-label="Datos comerciales del trámite"
      noValidate
    >
      {!hideHeader && (
        <div className="mb-3">
          <h4 className="text-sm font-bold">Datos comerciales</h4>
          <p className="text-[11px] opacity-60">
            Valor de la venta, causal e impuestos del traspaso.
          </p>
        </div>
      )}

      {error && (
        <div
          className="rounded-xl p-3 text-xs border mb-3 flex items-center justify-between gap-3"
          style={{
            borderColor: '#FF4E00',
            background: 'rgba(255,78,0,0.06)',
            color: '#FF4E00',
          }}
          role="alert"
          aria-live="polite"
        >
          <span>{error}</span>
          <button
            type="button"
            onClick={() => setError(null)}
            className="font-bold"
            aria-label="Descartar error"
          >
            ×
          </button>
        </div>
      )}

     <fieldset disabled={readOnly} className="contents">
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <div>
          <label htmlFor="comercial-valor" className="text-xs font-semibold mb-1.5 flex items-center gap-1.5">
            Valor de venta
            <span style={{ color: '#FF4E00' }} aria-label="obligatorio">*</span>
          </label>
          <div className="relative">
            <span
              className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-xs opacity-60"
              aria-hidden="true"
            >
              $
            </span>
            <input
              id="comercial-valor"
              type="text"
              inputMode="numeric"
              // Formato COP en vivo: el estado guarda el entero de pesos; aquí se
              // pinta agrupado con separador de miles y se parsea a dígitos.
              value={data.valorVenta != null ? groupThousands(String(data.valorVenta)) : ''}
              onChange={(e) => {
                const digits = digitsOnly(e.target.value);
                setData((d) => ({
                  ...d,
                  valorVenta: digits === '' ? null : Number(digits),
                }));
              }}
              placeholder="0"
              className={`${INPUT_BASE} pl-7 font-mono`}
              style={{ borderColor: '#DFE5ED' }}
            />
          </div>
        </div>

        <div>
          <label htmlFor="comercial-causal" className="text-xs font-semibold mb-1.5 flex items-center gap-1.5">
            Causal
            <span style={{ color: '#FF4E00' }} aria-label="obligatorio">*</span>
          </label>
          <select
            id="comercial-causal"
            value={data.causal ?? ''}
            onChange={(e) =>
              setData((d) => ({
                ...d,
                causal: (e.target.value || null) as CommercialCausal | null,
              }))
            }
            className={INPUT_BASE}
            style={{ borderColor: '#DFE5ED' }}
          >
            <option value="">Selecciona una causal…</option>
            {CAUSAL_OPTIONS.map((o) => (
              <option key={o.value} value={o.value}>
                {o.label}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label htmlFor="comercial-tasa" className="text-xs font-semibold mb-1.5 block">
            Tasa de impuesto <span className="opacity-50 font-normal">(%)</span>
          </label>
          <input
            id="comercial-tasa"
            type="number"
            min={0}
            step="0.01"
            value={data.tasaImpuesto ?? ''}
            onChange={(e) =>
              setData((d) => ({ ...d, tasaImpuesto: numberOrNull(e.target.value) }))
            }
            className={INPUT_BASE}
            style={{ borderColor: '#DFE5ED' }}
          />
        </div>

        <div>
          <label htmlFor="comercial-derechos" className="text-xs font-semibold mb-1.5 block">
            Derechos
          </label>
          <input
            id="comercial-derechos"
            type="number"
            min={0}
            value={data.derechos ?? ''}
            onChange={(e) =>
              setData((d) => ({ ...d, derechos: numberOrNull(e.target.value) }))
            }
            className={INPUT_BASE}
            style={{ borderColor: '#DFE5ED' }}
          />
        </div>

        <div className="md:col-span-2">
          <label htmlFor="comercial-metodo" className="text-xs font-semibold mb-1.5 block">
            Método de pago
          </label>
          <input
            id="comercial-metodo"
            type="text"
            value={data.metodoPago ?? ''}
            onChange={(e) =>
              setData((d) => ({ ...d, metodoPago: e.target.value || null }))
            }
            className={INPUT_BASE}
            style={{ borderColor: '#DFE5ED' }}
          />
        </div>
      </div>
     </fieldset>

      {/* Embebido en el wizard el guardado lo dispara el footer "Guardar y
          continuar" (vía save() del ref); aquí se omite el botón propio. */}
      {!readOnly && !embeddedInWizard && (
        <div className="flex items-center justify-between gap-3 mt-4">
          {saved ? (
            <span
              className="text-[11px] font-semibold"
              style={{ color: '#8CC63F' }}
              role="status"
              aria-live="polite"
            >
              Datos comerciales guardados ✓
            </span>
          ) : (
            <span className="text-[11px] opacity-50">
              {loading ? 'Cargando…' : ''}
            </span>
          )}
          <button
            type="submit"
            disabled={saving || !valid}
            className="px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
            style={{ background: '#557EFF' }}
          >
            {saving ? 'Guardando…' : 'Guardar datos comerciales'}
          </button>
        </div>
      )}
    </form>
  );
  },
);
