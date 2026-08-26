'use client';

import { useEffect, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { formatCOP } from '@/lib/format/currency';
import type {
  AvaluoSource,
  SuggestedCommercialValue,
} from '@/lib/api/types/procedure-runtime';
import { WIZARD_BTN_SOLID, WIZARD_CTA_GRADIENT } from './wizard-field-styles';

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
 * Columna "Avalúo Comercial" del prototipo Lovable (Traspaso · Datos Comerciales):
 * caja «Sugerido Fasecolda» + badge/aceptar. Las demás fuentes quedan como detalle secundario.
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
  const fuenteLabel = fuente ? (SOURCE_LABELS[fuente] ?? fuente) : 'Fasecolda';

  return (
    <section className="h-full" aria-label="Avalúo comercial sugerido">
      <h4 className="text-[13px] font-bold text-[#162744] dark:text-white">Avalúo Comercial</h4>
      <p className="mt-1 text-[12.5px] text-[#59677D] dark:text-white/60">
        Valor sugerido según tabla Fasecolda para el vehículo consultado.
      </p>

      {loading ? (
        <div className="mt-3 h-[72px] animate-pulse rounded-xl bg-black/5 dark:bg-white/5" aria-hidden="true" />
      ) : error ? (
        <p className="mt-3 text-xs text-[#59677D]" role="status">
          {error} Puedes ingresar el valor manualmente.
        </p>
      ) : (
        <>
          <div className="mt-3 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-[#DFE5ED] bg-[#F8FAFC] p-4 dark:border-white/10 dark:bg-white/5">
            <div>
              <p className="text-[11px] font-medium uppercase tracking-wide text-[#59677D] dark:text-white/60">
                Sugerido {fuenteLabel}
              </p>
              <p className="mt-0.5 text-xl font-bold text-[#162744] dark:text-white">
                {sugerido != null ? formatCOP(sugerido) : '—'}
              </p>
            </div>

            {sugerido != null && accepted && (
              <span
                className="inline-flex items-center rounded-full px-3 py-1 text-[11px] font-semibold text-white"
                style={{ background: '#8CC63F' }}
                role="status"
                aria-live="polite"
              >
                Valor sugerido aceptado
              </span>
            )}

            {sugerido != null && !accepted && (
              <button
                type="button"
                disabled={disabled}
                onClick={() => onAccept(sugerido, fuente ?? 'fasecolda', sugerido)}
                className="rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-50"
                style={{ background: WIZARD_CTA_GRADIENT }}
              >
                Aceptar valor sugerido
              </button>
            )}
          </div>

          {/* Fuentes adicionales (Feature #10707): detalle bajo la caja principal del prototipo. */}
          {(data?.sources?.length ?? 0) > 0 && (
            <ul className="mt-3 space-y-2">
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
    <li className="flex items-center justify-between gap-3 rounded-xl border border-[#DFE5ED] px-3 py-2 dark:border-white/10">
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
      <div className="flex shrink-0 items-center gap-3">
        <span className="font-mono text-xs">{ok ? formatCOP(source.value!) : '—'}</span>
        {ok && !accepted && (
          <button
            type="button"
            disabled={disabled}
            onClick={onUse}
            className="text-xs font-semibold disabled:opacity-40"
            style={{ color: WIZARD_BTN_SOLID }}
          >
            Usar
          </button>
        )}
      </div>
    </li>
  );
}
