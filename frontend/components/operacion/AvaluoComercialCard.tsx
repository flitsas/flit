'use client';

import { useEffect, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { formatCOP } from '@/lib/format/currency';
import type {
  AvaluoSource,
  SuggestedCommercialValue,
} from '@/lib/api/types/procedure-runtime';
import { WIZARD_CARD, WIZARD_CTA_GRADIENT } from './wizard-field-styles';
import { WizardCardHeader } from './wizard-atoms';

/** Etiquetas legibles por fuente; orden de render. */
const SOURCE_LABELS: Record<string, string> = {
  fasecolda: 'Fasecolda',
  base_gravable: 'Base gravable',
  mercado_libre: 'Mercado Libre',
};

interface Props {
  instanceId: string | null;
  disabled?: boolean;
  /**
   * HU #11019 — el valor de venta ya proviene de una sugerencia aceptada. Se sigue mostrando el avalúo
   * y sus fuentes (información útil), pero sin los botones de aceptar/usar: ya no hay nada que elegir.
   */
  accepted?: boolean;
  /** Aplica el valor elegido al campo "Valor de venta". */
  onAccept: (value: number, source: string, sugerido: number | null) => void;
}

/**
 * Sección "Avalúo comercial" (Feature #10707): consulta el valor sugerido multi-fuente y permite
 * aceptarlo. Nunca bloquea el paso: si una fuente falla o no tiene datos, se muestra su estado y
 * las demás siguen disponibles.
 */
export function AvaluoComercialCard({ instanceId, disabled = false, accepted = false, onAccept }: Props) {
  const [data, setData] = useState<SuggestedCommercialValue | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!instanceId) return;
    let active = true;
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const d = await tramitesClient.getSuggestedCommercialValue(instanceId);
        if (active) setData(d);
      } catch {
        // No bloquea el paso: solo se oculta la sugerencia.
        if (active) setError('No se pudo consultar el avalúo en este momento.');
      } finally {
        if (active) setLoading(false);
      }
    };
    void load();
    return () => {
      active = false;
    };
  }, [instanceId]);

  const sugerido = data?.sugerido ?? null;
  const fuente = data?.fuentePrincipal ?? null;

  return (
    <section className={WIZARD_CARD} aria-label="Avalúo comercial sugerido">
      <WizardCardHeader
        title="Avalúo comercial"
        subtitle="Valor de venta sugerido a partir de fuentes de avalúo."
        action={
          sugerido != null ? (
            <div className="text-right">
              <div className="text-xs uppercase tracking-wide opacity-70">Sugerido</div>
              <div className="text-sm font-bold font-mono" style={{ color: '#557EFF' }}>
                {formatCOP(sugerido)}
              </div>
              {fuente && (
                <div className="text-xs opacity-70">{SOURCE_LABELS[fuente] ?? fuente}</div>
              )}
            </div>
          ) : undefined
        }
      />

      {loading ? (
        <div className="space-y-2" aria-hidden="true">
          {[0, 1, 2].map((i) => (
            <div key={i} className="h-9 rounded-xl animate-pulse bg-black/5 dark:bg-white/5" />
          ))}
        </div>
      ) : error ? (
        <p className="text-xs opacity-70" role="status">
          {error} Puedes ingresar el valor manualmente.
        </p>
      ) : (
        <>
          <ul className="space-y-2">
            {(data?.sources ?? []).map((s) => (
              <SourceRow
                key={s.source}
                source={s}
                disabled={disabled}
                accepted={accepted}
                onUse={() => s.value != null && onAccept(s.value, s.source, sugerido)}
              />
            ))}
          </ul>

          {sugerido != null && accepted && (
            <p className="mt-3 text-right text-xs opacity-70" role="status">
              Valor sugerido aceptado.
            </p>
          )}

          {sugerido != null && !accepted && (
            <div className="flex justify-end mt-3">
              <button
                type="button"
                disabled={disabled}
                onClick={() => onAccept(sugerido, fuente ?? 'fasecolda', sugerido)}
                className="px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
                style={{ background: WIZARD_CTA_GRADIENT }}
              >
                Aceptar valor sugerido
              </button>
            </div>
          )}
        </>
      )}
    </section>
  );
}

function SourceRow({
  source,
  disabled,
  accepted,
  onUse,
}: {
  source: AvaluoSource;
  disabled: boolean;
  /** HU #11019 — con el valor ya aceptado la fila es informativa: sin botón «Usar». */
  accepted: boolean;
  onUse: () => void;
}) {
  const label = SOURCE_LABELS[source.source] ?? source.source;
  const ok = source.status === 'ok' && source.value != null;

  return (
    <li className="flex items-center justify-between gap-3 rounded-xl border px-3 py-2">
      <div className="min-w-0">
        <div className="text-xs font-semibold">{label}</div>
        {source.source === 'mercado_libre' && source.muestras != null && ok && (
          <div className="text-xs opacity-70">Mediana de {source.muestras} publicaciones</div>
        )}
        {!ok && (
          <div className="text-xs opacity-70">
            {source.status === 'no_data' ? 'Sin datos' : 'No disponible'}
          </div>
        )}
      </div>
      <div className="flex items-center gap-3 shrink-0">
        <span className="text-xs font-mono">{ok ? formatCOP(source.value!) : '—'}</span>
        {ok && !accepted && (
          <button
            type="button"
            disabled={disabled}
            onClick={onUse}
            className="text-xs font-semibold disabled:opacity-40"
            style={{ color: '#557EFF' }}
          >
            Usar
          </button>
        )}
      </div>
    </li>
  );
}
