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
import { WIZARD_INPUT, WIZARD_SELECT, WIZARD_CARD, WIZARD_CTA_GRADIENT } from './wizard-field-styles';
import { WizardCardHeader } from './wizard-atoms';
import { InlineAlert, INLINE_ALERT_TONES } from '@/components/atom/InlineAlert';
import { cn } from '@/lib/utils';

/** Handle imperativo: la shell del wizard dispara guardar+validar. */
export type CommercialFormHandle = WizardStepFormHandle;

interface Props {
  instanceId: string | null;
  /** Se invoca tras un guardado exitoso (la shell refresca el wizard). */
  onSaved?: () => void;
  /**
   * Oculta el título "Datos Comerciales" cuando el acordeón del wizard ya lo pinta.
   * El subtítulo de condiciones se mantiene en el cuerpo (prototipo Lovable).
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

/** Catálogo del prototipo Lovable (Traspaso · Método de pago). */
const METODO_PAGO_OPTIONS = [
  'PSE',
  'Transferencia bancaria',
  'Efectivo',
  'Tarjeta de crédito',
] as const;

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
 * Datos Comerciales del traspaso (prototipo Lovable):
 * columna izquierda Avalúo Comercial · columna derecha Condiciones Comerciales.
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

  const metodoKnown =
    data.metodoPago != null &&
    (METODO_PAGO_OPTIONS as readonly string[]).includes(data.metodoPago);

  return (
    <form
      onSubmit={handleSubmit}
      className={cn(embeddedInWizard || hideHeader ? 'space-y-4' : WIZARD_CARD)}
      aria-label="Datos comerciales del trámite"
      noValidate
    >
      {!hideHeader && (
        <WizardCardHeader
          title="Datos Comerciales"
          subtitle="Avalúo de referencia y condiciones económicas del traspaso."
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

      <div className="grid grid-cols-1 items-start gap-6 lg:grid-cols-2">
        {/* Izquierda — Avalúo (prototipo). En solo lectura no se ofrece captura por sugerencia. */}
        {!readOnly ? (
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
        ) : (
          <div aria-hidden="true" />
        )}

        {/* Derecha — Condiciones Comerciales */}
        <fieldset disabled={readOnly} className="h-full min-w-0 border-0 p-0">
          <legend className="sr-only">Condiciones Comerciales</legend>
          <h4 className="mb-3 text-[13px] font-bold text-[#162744] dark:text-white">
            Condiciones Comerciales
          </h4>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div>
              <label htmlFor="comercial-valor" className={REQUIRED_LABEL}>
                Valor de venta <span className="font-normal opacity-70">($)</span>
                <span style={{ color: '#FF4E00' }} aria-label="obligatorio">
                  *
                </span>
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
                  value={data.valorVenta != null ? groupThousands(String(data.valorVenta)) : ''}
                  onChange={(e) => {
                    const digits = digitsOnly(e.target.value);
                    setData((d) => ({
                      ...d,
                      valorVenta: digits === '' ? null : Number(digits),
                      valueOrigin: 'manual',
                    }));
                  }}
                  placeholder="Ej: 82.300.000"
                  className={`${INPUT_BASE} pl-7 font-mono`}
                />
              </div>
            </div>

            <div>
              <label htmlFor="comercial-causal" className={REQUIRED_LABEL}>
                Causal
                <span style={{ color: '#FF4E00' }} aria-label="obligatorio">
                  *
                </span>
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
                className={WIZARD_SELECT}
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
                Tasa de Impuesto <span className="font-normal opacity-70">(%)</span>
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
                placeholder="Ej: 1.0"
                aria-describedby="comercial-tasa-hint"
                className={INPUT_BASE}
              />
              <p id="comercial-tasa-hint" className="sr-only">
                Porcentaje del impuesto de vehículos aplicado sobre el valor de venta.
              </p>
            </div>

            <div>
              <label htmlFor="comercial-derechos" className={FIELD_LABEL}>
                Derechos <span className="font-normal opacity-70">($)</span>
              </label>
              <input
                id="comercial-derechos"
                type="text"
                inputMode="numeric"
                pattern="[0-9]*"
                autoComplete="off"
                value={data.derechos != null ? groupThousands(String(data.derechos)) : ''}
                onChange={(e) =>
                  setData((d) => ({ ...d, derechos: integerOrNull(e.target.value) }))
                }
                placeholder="Ej: 212.400"
                aria-describedby="comercial-derechos-hint"
                className={`${INPUT_BASE} font-mono`}
              />
              <p id="comercial-derechos-hint" className="sr-only">
                Derechos de tránsito (tarifa fija del organismo).
              </p>
            </div>

            <div className="sm:col-span-2">
              <label htmlFor="comercial-metodo" className={FIELD_LABEL}>
                Método de pago
              </label>
              <select
                id="comercial-metodo"
                value={metodoKnown ? (data.metodoPago ?? '') : data.metodoPago ? '__custom__' : ''}
                onChange={(e) => {
                  const v = e.target.value;
                  if (v === '' || v === '__custom__') {
                    setData((d) => ({ ...d, metodoPago: v === '__custom__' ? d.metodoPago : null }));
                    return;
                  }
                  setData((d) => ({ ...d, metodoPago: v }));
                }}
                className={WIZARD_SELECT}
              >
                <option value="">Selecciona…</option>
                {METODO_PAGO_OPTIONS.map((o) => (
                  <option key={o} value={o}>
                    {o}
                  </option>
                ))}
                {data.metodoPago && !metodoKnown && (
                  <option value="__custom__">{data.metodoPago}</option>
                )}
              </select>
            </div>
          </div>
        </fieldset>
      </div>

      {/* Embebido en el wizard el guardado lo dispara el footer "Guardar y
          continuar" (vía save() del ref); aquí se omite el botón propio. */}
      {!readOnly && !embeddedInWizard && (
        <div className="mt-4 flex items-center justify-between gap-3">
          {saved ? (
            <span
              className="text-xs font-semibold"
              style={{ color: 'var(--flit-success-ink)' }}
              role="status"
              aria-live="polite"
            >
              Datos comerciales guardados ✓
            </span>
          ) : (
            <span className="text-xs opacity-70">{loading ? 'Cargando…' : ''}</span>
          )}
          <button
            type="submit"
            disabled={saving || !valid}
            className="rounded-xl px-5 py-2 text-xs font-semibold text-white disabled:opacity-50"
            style={{ background: WIZARD_CTA_GRADIENT }}
          >
            {saving ? 'Guardando…' : 'Guardar datos comerciales'}
          </button>
        </div>
      )}
    </form>
  );
  },
);
