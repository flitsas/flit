'use client';

import { useEffect, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { formatCOP } from '@/lib/format/currency';
import type {
  AvaluoSource,
  SuggestedCommercialValue,
} from '@/lib/api/types/procedure-runtime';
import { WIZARD_BTN_SOLID, WIZARD_CTA_GRADIENT, WIZARD_LABEL } from './wizard-field-styles';
import { cn } from '@/lib/utils';

/** Etiquetas legibles por fuente; orden de render. */
const SOURCE_LABELS: Record<string, string> = {
  fasecolda: 'Fasecolda',
  base_gravable: 'Base gravable',
  mercado_libre: 'Mercado Libre',
};

/**
 * Las tres columnas de «Datos Comerciales» —sugerido, fuentes y valor de venta— comparten rejilla,
 * así que comparten también la altura de etiqueta y de caja: si cada una elegía la suya, los datos
 * quedaban a alturas distintas y la línea se leía torcida. `FIELD_BOX` iguala la caja al `<input>`
 * del valor de venta (mismo radio, borde, alto y centrado vertical).
 */
const LABEL = `${WIZARD_LABEL} mb-1.5`;
const FIELD_BOX =
  'flex h-9 items-center rounded-xl border border-[#DFE5ED] px-3 dark:border-white/10';

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
  /** Clases de la rejilla del formulario (el card ocupa dos de sus tres columnas). */
  className?: string;
}

/**
 * Columna "Avalúo Comercial" del prototipo Lovable (Traspaso · Datos Comerciales):
 * caja «Sugerido Fasecolda» + badge/aceptar. Las demás fuentes quedan como detalle secundario.
 */
export function AvaluoComercialCard({
  instanceId,
  disabled = false,
  accepted = false,
  onAccept,
  className,
}: Props) {
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
    /* Una sola línea con el resto de «Datos Comerciales»: la caja del sugerido y las fuentes son
       dos celdas hermanas de la rejilla del formulario (que aporta la tercera, «Valor de venta»).
       El título/subtítulo propios se retiraron: los repetía la cabecera del acordeón y solo
       robaban alto al card. */
    <section
      className={cn('grid h-full grid-cols-1 items-start gap-4 sm:grid-cols-2', className)}
      aria-label="Avalúo comercial sugerido"
    >
      {loading ? (
        <div className="sm:col-span-2">
          <span className={LABEL}>Avalúo sugerido</span>
          <div className={cn(FIELD_BOX, 'animate-pulse bg-black/5 dark:bg-white/5')} aria-hidden="true" />
        </div>
      ) : error ? (
        <p className="text-xs text-[#59677D] sm:col-span-2" role="status">
          {error} Puedes ingresar el valor manualmente.
        </p>
      ) : (
        <>
          <div className="min-w-0">
            <span className={LABEL}>
              Avalúo sugerido <span className="font-normal opacity-70">({fuenteLabel})</span>
            </span>
            <div className={cn(FIELD_BOX, 'justify-between gap-3 bg-[#F8FAFC] dark:bg-white/5')}>
              <span className="truncate text-sm font-bold text-[#162744] dark:text-white">
                {sugerido != null ? formatCOP(sugerido) : '—'}
              </span>

              {sugerido != null && accepted && (
                <span
                  className="shrink-0 rounded-full px-2.5 py-0.5 text-[11px] font-semibold text-white"
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
                  className="shrink-0 rounded-lg px-2.5 py-1 text-[11px] font-semibold text-white disabled:opacity-50"
                  style={{ background: WIZARD_CTA_GRADIENT }}
                >
                  Aceptar valor sugerido
                </button>
              )}
            </div>
          </div>

          {/* Fuentes adicionales (Feature #10707): celda propia, al lado del sugerido. Lleva su
              propia etiqueta —«Fuentes consultadas»— porque sin ella la fila suelta no dice de qué
              habla: son los proveedores de avalúo que se consultaron y qué devolvió cada uno. */}
          {(data?.sources?.length ?? 0) > 0 && (
            <div className="min-w-0">
              <span className={LABEL}>Fuentes consultadas</span>
              <ul className="space-y-1.5">
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
    <li className={cn(FIELD_BOX, 'justify-between gap-3')}>
      <div className="flex min-w-0 items-baseline gap-2">
        <span className="truncate text-xs font-semibold">{label}</span>
        {source.source === 'mercado_libre' && source.muestras != null && ok && (
          <span className="truncate text-[11px] opacity-70">Mediana de {source.muestras} publicaciones</span>
        )}
        {!ok && (
          <span className="text-[11px] opacity-70">
            {source.status === 'no_data' ? 'Sin datos' : 'No disponible'}
          </span>
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
