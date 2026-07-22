import type {
  ConformationRuleItem,
  ProcedureEntityCode,
} from '@/lib/api/types/procedure-parametrization';

interface Step3Props {
  rules: ConformationRuleItem[];
  onToggle: (code: ProcedureEntityCode) => void;
  /** FEATURE-08 / CFD-05 — patch de banderas por-actor del validation_profile. */
  onProfileChange?: (code: ProcedureEntityCode, patch: Record<string, boolean>) => void;
}

const ENTITY_LABELS: Record<ProcedureEntityCode, { label: string; description: string }> = {
  VEHICLE: { label: 'Vehículo', description: 'Datos e historial del vehículo' },
  OWNER: { label: 'Propietario', description: 'Titular actual del bien' },
  BUYER: { label: 'Comprador', description: 'Nuevo adquirente del vehículo' },
  LESSEE: { label: 'Arrendatario', description: 'Aplica en leasing o arrendamiento' },
};

// Banderas por-actor del validation_profile (CFD-05). No aplican a VEHICLE (no es un actor).
const ACTOR_FLAGS: { key: string; label: string; description: string }[] = [
  { key: 'allowsNaturalPerson', label: 'Persona natural', description: 'Permite que el actor sea persona natural.' },
  { key: 'allowsJuridicalPerson', label: 'Persona jurídica', description: 'Permite persona jurídica (usa representante legal).' },
  { key: 'allowsMultiple', label: 'Permite múltiples', description: 'Se pueden agregar varios (p. ej. varios propietarios).' },
  { key: 'requiresRunt', label: 'Requiere RUNT', description: 'Exige consultar el RUNT del actor.' },
];

function flag(rule: ConformationRuleItem, key: string): boolean {
  return rule.validationProfile?.[key] === true;
}

export function Step3Aristas({ rules, onToggle, onProfileChange }: Step3Props) {
  return (
    <div className="space-y-5">
      <div>
        <h2 className="text-base font-bold mb-1">Matriz de conformación</h2>
        <p className="text-xs opacity-60">
          Selecciona las aristas (entidades) que participan y, por cada actor, cómo se valida
          (persona natural/jurídica, múltiples, RUNT).
        </p>
      </div>

      <div className="space-y-2">
        {rules.map((rule) => {
          const meta = ENTITY_LABELS[rule.procedureEntityCode];
          const isActor = rule.procedureEntityCode !== 'VEHICLE';
          return (
            <div
              key={rule.procedureEntityCode}
              className="rounded-xl border transition"
              style={{
                borderColor: rule.isActive ? '#557EFF' : '#DFE5ED',
                background: rule.isActive ? 'rgba(85,126,255,0.06)' : 'transparent',
              }}
            >
              <label
                htmlFor={`arista-${rule.procedureEntityCode}`}
                className="flex items-center gap-4 p-4 cursor-pointer"
              >
                <input
                  id={`arista-${rule.procedureEntityCode}`}
                  type="checkbox"
                  checked={rule.isActive}
                  onChange={() => onToggle(rule.procedureEntityCode)}
                  className="h-4 w-4 rounded"
                  aria-label={`Activar arista ${meta.label}`}
                />
                <div className="flex-1 min-w-0">
                  <p className="text-xs font-semibold">{meta.label}</p>
                  <p className="text-[10px] opacity-60">{meta.description}</p>
                </div>
                <span
                  className="text-[10px] font-bold px-2 py-0.5 rounded-full"
                  style={{
                    background: rule.isActive ? 'rgba(85,126,255,0.15)' : '#DFE5ED',
                    color: rule.isActive ? '#557EFF' : '#162744',
                  }}
                >
                  {rule.isActive ? 'Activa' : 'Inactiva'}
                </span>
              </label>

              {/* CFD-05 — banderas por-actor, visibles solo cuando la arista de un actor está activa. */}
              {isActor && rule.isActive && (
                <div className="px-4 pb-4 grid grid-cols-1 sm:grid-cols-2 gap-2">
                  {ACTOR_FLAGS.map((f) => (
                    <label
                      key={f.key}
                      className="flex items-start gap-2 rounded-lg border p-2.5 bg-white dark:bg-[#0B0F14]"
                    >
                      <input
                        type="checkbox"
                        className="mt-0.5 h-3.5 w-3.5"
                        checked={flag(rule, f.key)}
                        onChange={(e) =>
                          onProfileChange?.(rule.procedureEntityCode, { [f.key]: e.target.checked })
                        }
                        aria-label={`${meta.label} — ${f.label}`}
                      />
                      <span>
                        <span className="block text-[11px] font-semibold">{f.label}</span>
                        <span className="block text-[10px] opacity-55">{f.description}</span>
                      </span>
                    </label>
                  ))}
                </div>
              )}
            </div>
          );
        })}
      </div>

      <p className="text-[10px] opacity-50">
        Mínimo una arista activa requerida para publicar. Las banderas por-actor definen la
        multiplicidad (varios propietarios/compradores) y el tipo de persona en cualquier trámite.
      </p>
    </div>
  );
}
