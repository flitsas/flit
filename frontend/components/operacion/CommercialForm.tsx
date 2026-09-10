'use client';

import { forwardRef, useEffect, useImperativeHandle, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { digitsOnly, groupThousands } from '@/lib/format/currency';
import { usePendingChanges } from './pending-changes';
import { useWizardReadOnly } from './WizardReadOnlyContext';
import { AvaluoComercialCard } from './AvaluoComercialCard';
import type { WizardStepFormHandle } from './wizard-step-form';
import type { CommercialData } from '@/lib/api/types/procedure-runtime';
import { WIZARD_INPUT, WIZARD_CARD, WIZARD_CTA_GRADIENT } from './wizard-field-styles';
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

/** Causal que el API sigue exigiendo; en captura ya no se elige. */
const CAUSAL_POR_DEFECTO = 'COMPRAVENTA' as const;

const INPUT_BASE = WIZARD_INPUT;
const REQUIRED_LABEL = 'text-xs font-medium text-[#59677D] dark:text-white/70 mb-1.5 flex items-center gap-1.5';

const EMPTY: CommercialData = {
  valorVenta: null,
  causal: null,
  tasaImpuesto: null,
  derechos: null,
  metodoPago: null,
};

/**
 * Datos Comerciales del traspaso: avalúo + valor de venta.
 * Causal (siempre compraventa), tasa, derechos y método de pago no se capturan aquí:
 * el PUT los reenvía si ya estaban persistidos, para no borrar borradores viejos.
 */
export const CommercialForm = forwardRef<CommercialFormHandle, Props>(
  function CommercialForm(
    { instanceId, onSaved, hideHeader = false, embeddedInWizard = false },
    ref,
  ) {
  // Solo lectura (Track C): inputs deshabilitados + sin botón guardar.
  const readOnly = useWizardReadOnly();
  const [data, setData] = useState<CommercialData>(EMPTY);
  /**
   * Bug #11614 — captura del usuario sin persistir. Solo la edición del gestor la marca (nunca la
   * carga desde el backend), y la shell la consulta vía `hasPendingChanges` antes de cambiar de
   * paso por el stepper o por "Anterior", donde este formulario se desmonta. La marca se limpia
   * siempre con `beginSettle()` tomado ANTES del await, para que una carga (o un PUT) que resuelve
   * tarde no borre lo que el gestor escribió mientras tanto.
   */
  const pending = usePendingChanges();
  /** Edición del usuario: muta el estado y marca pendiente de guardar. */
  const editData = (updater: (d: CommercialData) => CommercialData) => {
    pending.markDirty();
    setData(updater);
  };
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
      // ANTES del await: si el gestor escribe mientras la carga viaja, la marca sobrevive.
      const settle = pending.beginSettle();
      try {
        const d = await tramitesClient.getCommercial(instanceId);
        if (active && d) {
          setData({ ...EMPTY, ...d });
          // Lo cargado ES lo persistido: no cuenta como cambio pendiente (salvo que el gestor
          // haya capturado algo mientras la petición estaba en vuelo).
          settle();
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
    // `pending` es estable (instancia única por montaje): no re-dispara la carga.
  }, [instanceId, pending]);

  const valid = data.valorVenta != null && data.valorVenta > 0;

  // Valida + persiste. Núcleo compartido por el submit propio y el save() del
  // ref (footer "Guardar y continuar" del wizard). Devuelve true si persistió.
  const submit = async (): Promise<boolean> => {
    if (!instanceId) return false;
    if (!valid) {
      setError('Ingresa el valor de venta para continuar.');
      return false;
    }
    setSaving(true);
    setSaved(false);
    setError(null);
    // El payload queda congelado aquí: lo que se teclee mientras el PUT viaja sigue pendiente.
    const settle = pending.beginSettle();
    try {
      await tramitesClient.putCommercial(instanceId, {
        ...data,
        causal: CAUSAL_POR_DEFECTO,
      });
      setSaved(true);
      settle();
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

  useImperativeHandle(ref, () => ({ save: submit, hasPendingChanges: pending.hasPendingChanges }));

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    await submit();
  };

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

      {/* Los tres datos del paso —sugerido, fuentes y valor de venta— caben en una sola línea:
          el avalúo ocupa dos columnas (aporta sugerido + fuentes) y el valor de venta la tercera.
          Antes cada mitad apilaba título, subtítulo y cajas, y el card crecía sin necesidad. */}
      <div className="grid grid-cols-1 items-start gap-4 lg:grid-cols-3">
        {/* Avalúo (prototipo). En solo lectura no se ofrece captura por sugerencia. */}
        {!readOnly ? (
          <AvaluoComercialCard
            className="lg:col-span-2"
            instanceId={instanceId}
            disabled={readOnly}
            accepted={data.valueOrigin === 'suggestion'}
            onAccept={(value, source, sugerido) =>
              editData((d) => ({
                ...d,
                valorVenta: value,
                valueOrigin: 'suggestion',
                suggestedSource: source,
                suggestedValue: sugerido,
              }))
            }
          />
        ) : (
          <div className="lg:col-span-2" aria-hidden="true" />
        )}

        {/* Tercera columna — Condiciones Comerciales */}
        <fieldset disabled={readOnly} className="min-w-0 border-0 p-0">
          <legend className="sr-only">Condiciones Comerciales</legend>
          <div className="grid grid-cols-1 gap-4">
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
                    editData((d) => ({
                      ...d,
                      valorVenta: digits === '' ? null : Number(digits),
                      valueOrigin: 'manual',
                    }));
                  }}
                  placeholder="Ej: 82.300.000"
                  /* `h-9`: misma altura que las cajas del avalúo, para que los tres datos de la
                     línea queden a la misma cota. */
                  className={`${INPUT_BASE} h-9 pl-7 font-mono`}
                />
              </div>
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
