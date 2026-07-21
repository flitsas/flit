'use client';

interface BiometricSectionProps {
  /** Actores que requieren biometría (gate_profile.biometricActors). */
  actors: string[];
  approvedActors?: string[];
}

const ACTOR_LABELS: Record<string, string> = {
  OWNER: 'Propietario',
  BUYER: 'Comprador',
  SELLER: 'Vendedor',
  LESSEE: 'Locatario',
};

/**
 * FEATURE-08 / HU-FE-03 (CFD-07) — sección de validación de identidad (biometría) del wizard
 * dinámico. Se registra bajo <code>section_type='biometric'</code> y muestra el estado por actor.
 */
export function BiometricSection({ actors, approvedActors = [] }: BiometricSectionProps) {
  const approved = new Set(approvedActors);

  return (
    <section aria-label="Validación de identidad" className="space-y-3">
      <h2 className="text-base font-bold mb-1">Identidad</h2>
      {actors.length === 0 && (
        <p className="text-xs opacity-50">Este tipo no requiere validación biométrica.</p>
      )}
      <ul className="space-y-2">
        {actors.map((actor) => {
          const ok = approved.has(actor);
          return (
            <li
              key={actor}
              data-testid={`biometric-${actor}`}
              className="flex items-center justify-between rounded-xl p-3 border"
              style={{ borderColor: '#DFE5ED' }}
            >
              <span className="text-xs font-semibold">{ACTOR_LABELS[actor] ?? actor}</span>
              <span
                className="text-[10px] font-bold px-2 py-0.5 rounded-full"
                style={{
                  background: ok ? 'rgba(140,198,63,0.15)' : 'rgba(249,172,0,0.15)',
                  color: ok ? '#5a8a1f' : '#a86f00',
                }}
              >
                {ok ? 'Aprobada' : 'Pendiente'}
              </span>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
