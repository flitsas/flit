'use client';

import { forwardRef, useEffect, useImperativeHandle, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { digitsOnly, groupThousands, sanitizeDecimalInput } from '@/lib/format/currency';
import { useWizardReadOnly } from './WizardReadOnlyContext';
import { AvaluoComercialCard } from './AvaluoComercialCard';
import type { WizardStepFormHandle } from './wizard-step-form';
import type {
  CommercialCausal,
  CommercialData,
} from '@/lib/api/types/procedure-runtime';
import { WIZARD_INPUT, WIZARD_CARD } from './wizard-field-styles';
import { WizardCardHeader } from './wizard-atoms';
import { InlineAlert, INLINE_ALERT_TONES } from '@/components/atom/InlineAlert';

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

const INPUT_BASE = WIZARD_INPUT;
/** Rótulo de campo: mismo color/tamaño del sistema (`wizard-field-styles`), con hueco para el asterisco. */
const FIELD_LABEL = 'text-xs font-medium text-[#59677D] dark:text-white/70 mb-1.5 block';
const REQUIRED_LABEL = 'text-xs font-medium text-[#59677D] dark:text-white/70 mb-1.5 flex items-center gap-1.5';

const EMPTY: CommercialData = {
  valorVenta: null,
  causal: null,
  tasaImpuesto: null,
  derechos: null,
  metodoPago: null,
};

function integerOrNull(v: string): number | null {
  const digits = digitsOnly(v);
  if (digits === '') return null;
  const n = Number(digits);
  return Number.isFinite(n) ? n : null;
}

function decimalOrNull(v: string): number | null {
  const cleaned = sanitizeDecimalInput(v);
  if (cleaned === '' || cleaned === '.') return null;
  const n = Number(cleaned);
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
  /** Borrador de tasa para permitir tipar "1." sin perder el separador. */
  const [tasaText, setTasaText] = useState('');
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
        if (active && d) {
          setData({ ...EMPTY, ...d });
          setTasaText(d.tasaImpuesto != null ? String(d.tasaImpuesto) : '');
        }
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
      className={WIZARD_CARD}
      aria-label="Datos comerciales del trámite"
      noValidate
    >
      {!hideHeader && (
        <WizardCardHeader
          title="Datos comerciales"
          subtitle="Valor de la venta, causal e impuestos del traspaso."
        />
      )}

      {error && (
        <InlineAlert
          tone="error"
          className="mb-3"
          action={
            <button
              type="button"
              onClick={() => setError(null)}
              className="font-bold"
              style={{ color: INLINE_ALERT_TONES.error.color }}
              aria-label="Descartar error"
            >
              ×
            </button>
          }
        >
          {error}
        </InlineAlert>
      )}

     <fieldset disabled={readOnly} className="contents">
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-4">
        <div>
          <label htmlFor="comercial-valor" className={REQUIRED_LABEL}>
            Valor de venta
            <span style={{ color: '#FF4E00' }} aria-label="obligatorio">*</span>
          </label>
          <div className="relative">
            <span
              className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-xs opacity-70"
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
                  // Edición manual: el valor deja de ser el sugerido (trazabilidad).
                  valueOrigin: 'manual',
                }));
              }}
              placeholder="0"
              className={`${INPUT_BASE} pl-7 font-mono`}
            />
          </div>
        </div>

        <div>
          <label htmlFor="comercial-causal" className={REQUIRED_LABEL}>
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
          <label htmlFor="comercial-tasa" className={FIELD_LABEL}>
            Tasa de impuesto <span className="opacity-70 font-normal">(%)</span>
          </label>
          <input
            id="comercial-tasa"
            type="text"
            inputMode="decimal"
            autoComplete="off"
            value={tasaText}
            onChange={(e) => {
              const raw = sanitizeDecimalInput(e.target.value);
              setTasaText(raw);
              setData((d) => ({ ...d, tasaImpuesto: decimalOrNull(raw) }));
            }}
            className={INPUT_BASE}
          />
        </div>

        <div>
          <label htmlFor="comercial-derechos" className={FIELD_LABEL}>
            Derechos
          </label>
          <input
            id="comercial-derechos"
            type="text"
            inputMode="numeric"
            pattern="[0-9]*"
            autoComplete="off"
            value={data.derechos ?? ''}
            onChange={(e) =>
              setData((d) => ({ ...d, derechos: integerOrNull(e.target.value) }))
            }
            className={INPUT_BASE}
          />
        </div>

        <div>
          <label htmlFor="comercial-metodo" className={FIELD_LABEL}>
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
          />
        </div>
      </div>
     </fieldset>

      {/* Avalúo comercial: ayuda de captura, debajo de la rejilla de datos. */}
      {!readOnly && (
        <div className="mt-4">
          <AvaluoComercialCard
            instanceId={instanceId}
            disabled={readOnly}
            accepted={data.valueOrigin === 'suggestion'}
            onAccept={(value, source, sugerido) =>
              setData((d) => ({
                ...d,
                valorVenta: value,
                valueOrigin: 'suggestion',
                suggestedSource: source,
                suggestedValue: sugerido,
              }))
            }
          />
        </div>
      )}

      {/* Embebido en el wizard el guardado lo dispara el footer "Guardar y
          continuar" (vía save() del ref); aquí se omite el botón propio. */}
      {!readOnly && !embeddedInWizard && (
        <div className="flex items-center justify-between gap-3 mt-4">
          {saved ? (
            <span
              className="text-xs font-semibold"
              style={{ color: '#8CC63F' }}
              role="status"
              aria-live="polite"
            >
              Datos comerciales guardados ✓
            </span>
          ) : (
            <span className="text-xs opacity-70">
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
