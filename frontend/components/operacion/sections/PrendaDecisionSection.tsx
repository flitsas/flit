'use client';

export type PrendaDecision = 'con_prenda' | 'sin_prenda';

interface PrendaDecisionSectionProps {
  decision?: PrendaDecision | null;
  onDecide?: (decision: PrendaDecision) => void;
}

const OPTIONS: { value: PrendaDecision; label: string; description: string }[] = [
  { value: 'con_prenda', label: 'Con prenda', description: 'El vehículo queda con gravamen inscrito.' },
  { value: 'sin_prenda', label: 'Sin prenda', description: 'El trámite continúa sin inscribir prenda.' },
];

/**
 * FEATURE-08 / HU-FE-05 (CFD-09) — sección de decisión de prenda del wizard dinámico
 * (<code>section_type='prenda_decision'</code>), con semántica específica del gravamen.
 */
export function PrendaDecisionSection({ decision, onDecide }: PrendaDecisionSectionProps) {
  return (
    <section aria-label="Decisión de prenda" className="space-y-3">
      <h2 className="text-base font-bold mb-1">Decisión de prenda</h2>
      <p className="text-xs opacity-60">Indica si el trámite inscribe prenda sobre el vehículo.</p>

      <div className="space-y-2">
        {OPTIONS.map((opt) => {
          const selected = decision === opt.value;
          return (
            <label
              key={opt.value}
              htmlFor={`prenda-${opt.value}`}
              className="flex items-center gap-4 rounded-xl p-4 border cursor-pointer transition"
              style={{
                borderColor: selected ? '#557EFF' : '#DFE5ED',
                background: selected ? 'rgba(85,126,255,0.06)' : 'transparent',
              }}
            >
              <input
                id={`prenda-${opt.value}`}
                type="radio"
                name="prenda-decision"
                checked={selected}
                onChange={() => onDecide?.(opt.value)}
                aria-label={opt.label}
              />
              <div className="flex-1 min-w-0">
                <p className="text-xs font-semibold">{opt.label}</p>
                <p className="text-[10px] opacity-60">{opt.description}</p>
              </div>
            </label>
          );
        })}
      </div>
    </section>
  );
}
