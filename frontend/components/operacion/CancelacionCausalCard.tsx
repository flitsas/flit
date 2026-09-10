'use client';

import { useEffect, useState } from 'react';
import { cn } from '@/lib/utils';
import { tramitesClient } from '@/lib/api/tramites-client';
import {
  CANCELACION_CAUSALES,
  CANCELACION_CAUSAL_FIELD_KEY,
  type CancelacionCausal,
  cancelacionObservacionFur,
  documentosDeCausal,
  etiquetaDocumento,
  parseCancelacionCausal,
} from '@/lib/catalogs/cancelacion';
import { WIZARD_INPUT, WIZARD_LABEL } from './wizard-field-styles';

/**
 * Causal del trámite `CANCELACION_MATRICULA` (familia OTROS): por qué se cancela la matrícula.
 *
 * <p>El formulario oficial tiene UNA casilla (la 13) para cuatro situaciones que el organismo trata
 * distinto —lo ordena un juez, el vehículo se destruyó por fuerza mayor o en un accidente, o el
 * propietario lo saca de circulación— y cada una se acredita con documentos diferentes. Hasta ahora
 * FLIT no lo preguntaba: el checklist pedía lo mismo para las cuatro y el FUR salía mudo sobre cuál
 * era, así que el organismo tenía que deducirla de los anexos.</p>
 *
 * <p>Los documentos NO se suben aquí: la causal los enciende en «Gestión de documentos», que está en
 * este mismo paso. Son hasta tres por causal, y duplicar el cargador competiría con el checklist —a
 * diferencia del blindaje, que tiene un único certificado y sí lo lleva junto a su opción.</p>
 */
export function CancelacionCausalCard({
  instanceId,
  readOnly,
  onCompletenessChange,
  onCausalChange,
}: {
  instanceId: string | null;
  readOnly: boolean;
  /** Notifica si hay causal declarada (gate de Continuar). */
  onCompletenessChange?: (complete: boolean) => void;
  /** Solo en cambios del gestor: el checklist que exige la causal tiene que recargarse. */
  onCausalChange?: (causal: CancelacionCausal | null) => void;
}) {
  const [causal, setCausal] = useState<CancelacionCausal | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!instanceId) return;
    let active = true;
    void tramitesClient
      .getInstance(instanceId)
      .then((d) => {
        if (!active) return;
        setCausal(
          parseCancelacionCausal(
            d?.fieldValues?.find((f) => f.fieldKey === CANCELACION_CAUSAL_FIELD_KEY)?.valueText,
          ),
        );
      })
      .catch(() => {});
    return () => {
      active = false;
    };
  }, [instanceId]);

  useEffect(() => {
    onCompletenessChange?.(causal !== null);
  }, [causal, onCompletenessChange]);

  const elegir = async (valor: string) => {
    const nueva = parseCancelacionCausal(valor);
    if (!instanceId || !nueva || nueva === causal) return;
    setSaving(true);
    setError(null);
    try {
      await tramitesClient.patchFieldValues(instanceId, [
        {
          formFieldId: null,
          fieldKey: CANCELACION_CAUSAL_FIELD_KEY,
          valueText: nueva,
          valueJson: null,
        },
      ]);
      setCausal(nueva);
      // Solo aquí, no al hidratar: el checklist que llega del servidor ya viene resuelto con la
      // causal guardada, y avisar en el montaje lo haría recargarse sin que nada hubiera cambiado.
      onCausalChange?.(nueva);
    } catch {
      setError('No se pudo guardar la causal de cancelación. Reintenta.');
    } finally {
      setSaving(false);
    }
  };

  const documentos = documentosDeCausal(causal);
  const disabled = readOnly || saving || !instanceId;

  return (
    <div className="space-y-4">
      <p className="text-[13px] opacity-70">
        Escoge por qué se cancela la matrícula: de la causal dependen los documentos obligatorios y
        lo que el FUR va a imprimir.
      </p>

      {error && (
        <p className="text-xs font-medium" style={{ color: '#FF4E00' }} role="alert">
          {error}
        </p>
      )}

      <div
        className={cn(
          'relative flex flex-col rounded-xl bg-white p-4 shadow-sm transition hover:shadow-md sm:max-w-sm dark:bg-[#162744]',
          causal ? 'border' : 'border-2 border-dashed hover:border-[#557EFF] hover:bg-[#F0F5FF]',
        )}
        style={{ borderColor: '#E2E8F0' }}
      >
        <span
          className={cn(
            'absolute right-3 top-3 whitespace-nowrap rounded-full px-2.5 py-0.5 text-xs',
            causal ? 'font-semibold text-white' : 'bg-red-50 font-medium text-red-600',
          )}
          style={causal ? { background: '#8CC63F' } : undefined}
        >
          {causal ? 'Validado' : '* Obligatorio'}
        </span>

        <p className="pr-28 text-[13px] font-semibold leading-tight" style={{ color: '#162744' }}>
          Causal de cancelación
        </p>

        <div className="mt-3 space-y-1.5">
          <label htmlFor="cancelacion-causal" className={WIZARD_LABEL}>
            Motivo de la cancelación *
          </label>
          <select
            id="cancelacion-causal"
            value={causal ?? ''}
            disabled={disabled}
            onChange={(e) => void elegir(e.target.value)}
            className={`${WIZARD_INPUT} disabled:opacity-60`}
            aria-invalid={causal === null}
          >
            <option value="">Selecciona una causal…</option>
            {CANCELACION_CAUSALES.map((c) => (
              <option key={c.codigo} value={c.codigo}>
                {c.label}
              </option>
            ))}
          </select>
          {causal === null && (
            <p className="text-[11px] font-medium" style={{ color: '#FF4E00' }}>
              {instanceId
                ? 'Escoge la causal por la que se cancela.'
                : 'Guarda el trámite para poder declarar la causal.'}
            </p>
          )}
        </div>

        {documentos.length > 0 && (
          <div className="mt-4">
            <p className="text-[11px] font-semibold uppercase tracking-wide opacity-60">
              Documentos obligatorios
            </p>
            <ul className="mt-1 space-y-0.5">
              {documentos.map((d) => (
                <li key={d} className="text-[12px] opacity-80">
                  · {etiquetaDocumento(d)}
                </li>
              ))}
            </ul>
            <p className="mt-1 text-[11px] opacity-60">Se cargan en «Gestión de documentos».</p>
          </div>
        )}
      </div>

      <p
        aria-live="polite"
        className={cn('rounded-xl px-3 py-2 text-xs', causal ? 'font-medium' : 'opacity-70')}
        style={causal ? { background: 'rgba(85,126,255,0.08)', color: '#557EFF' } : undefined}
      >
        {causal
          ? `Se registrará en el FUR — ${cancelacionObservacionFur(causal)}`
          : 'Sin causal declarada: el FUR no puede decir por qué se cancela la matrícula.'}
      </p>
    </div>
  );
}
