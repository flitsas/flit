'use client';

import type { ConformationRuleProfile } from '@/lib/api/types/procedure-parametrization-f08';

export interface ActorValue {
  fullName?: string;
  documentNumber?: string;
  personType?: 'natural' | 'juridical';
  legalRepresentative?: string;
}

interface ActorFormSectionProps {
  /** Reglas de conformación del estado del wizard (incluye VEHICLE, que no es actor). */
  conformationRules: ConformationRuleProfile[];
  values?: Record<string, ActorValue>;
  onChange?: (entityCode: string, value: ActorValue) => void;
  /** Invocado al pulsar "Agregar {actor}" para actores con allowsMultiple. */
  onAddActor?: (entityCode: string) => void;
}

const ACTOR_LABELS: Record<string, string> = {
  OWNER: 'Propietario',
  BUYER: 'Comprador',
  SELLER: 'Vendedor',
  LESSEE: 'Locatario',
};

/** VEHICLE no es un actor: es la arista del vehículo, se renderiza en la consulta. */
const NON_ACTOR_ENTITIES = new Set(['VEHICLE']);

function asBool(profile: Record<string, unknown>, key: string): boolean {
  return profile[key] === true;
}

/**
 * FEATURE-08 / HU-FE-02 (CFD-05) — renderiza N actores configurables a partir de las
 * conformationRules del estado del wizard, incluyendo la arista LESSEE. Adapta la persona
 * (natural/jurídica) según los flags del validation_profile.
 */
export function ActorFormSection({
  conformationRules,
  values = {},
  onChange,
  onAddActor,
}: ActorFormSectionProps) {
  const actorRules = conformationRules.filter((r) => !NON_ACTOR_ENTITIES.has(r.entityCode));

  return (
    <section aria-label="Actores del trámite" className="space-y-4">
      <div>
        <h2 className="text-base font-bold mb-1">Actores</h2>
        <p className="text-xs opacity-60">Participantes requeridos según la configuración del tipo.</p>
      </div>

      {actorRules.length === 0 && (
        <p className="text-xs opacity-50">Este tipo no requiere actores configurados.</p>
      )}

      {actorRules.map((rule) => {
        const label = ACTOR_LABELS[rule.entityCode] ?? rule.entityCode;
        const profile = rule.validationProfile ?? {};
        const allowsNatural = asBool(profile, 'allowsNaturalPerson');
        const allowsJuridical = asBool(profile, 'allowsJuridicalPerson');
        const allowsMultiple = asBool(profile, 'allowsMultiple');
        const showPersonType = allowsNatural && allowsJuridical;
        const value = values[rule.entityCode] ?? {};
        const effectivePersonType =
          value.personType ?? (allowsJuridical && !allowsNatural ? 'juridical' : 'natural');
        const isJuridical = effectivePersonType === 'juridical';

        return (
          <fieldset
            key={rule.entityCode}
            className="rounded-2xl p-4 border space-y-3"
            data-testid={`actor-${rule.entityCode}`}
          >
            <legend className="text-xs font-bold px-1">{label}</legend>

            {showPersonType && (
              <div className="flex items-center gap-3">
                <span className="text-[10px] font-semibold opacity-70">Tipo de persona</span>
                {(['natural', 'juridical'] as const).map((pt) => (
                  <label key={pt} htmlFor={`person-${rule.entityCode}-${pt}`} className="flex items-center gap-1 text-[11px]">
                    <input
                      id={`person-${rule.entityCode}-${pt}`}
                      type="radio"
                      name={`person-${rule.entityCode}`}
                      checked={(value.personType ?? 'natural') === pt}
                      onChange={() => onChange?.(rule.entityCode, { ...value, personType: pt })}
                      aria-label={`${label} persona ${pt === 'natural' ? 'natural' : 'jurídica'}`}
                    />
                    {pt === 'natural' ? 'Natural' : 'Jurídica'}
                  </label>
                ))}
              </div>
            )}

            <div className="space-y-1">
              <label htmlFor={`name-${rule.entityCode}`} className="text-[11px] font-semibold">
                {isJuridical ? 'Razón social' : 'Nombre completo'}
              </label>
              <input
                id={`name-${rule.entityCode}`}
                type="text"
                value={value.fullName ?? ''}
                onChange={(e) => onChange?.(rule.entityCode, { ...value, fullName: e.target.value })}
                aria-label={`Nombre de ${label}`}
                className="w-full px-3 py-2 rounded-xl border outline-none focus:border-[#557EFF]"
              />
            </div>

            <div className="space-y-1">
              <label htmlFor={`doc-${rule.entityCode}`} className="text-[11px] font-semibold">
                {isJuridical ? 'NIT' : 'Documento'}
              </label>
              <input
                id={`doc-${rule.entityCode}`}
                type="text"
                value={value.documentNumber ?? ''}
                onChange={(e) => onChange?.(rule.entityCode, { ...value, documentNumber: e.target.value })}
                aria-label={isJuridical ? `NIT de ${label}` : `Documento de ${label}`}
                className="w-full px-3 py-2 rounded-xl border outline-none focus:border-[#557EFF]"
              />
            </div>

            {isJuridical && (
              <div className="space-y-1">
                <label htmlFor={`rep-${rule.entityCode}`} className="text-[11px] font-semibold">
                  Representante legal
                </label>
                <input
                  id={`rep-${rule.entityCode}`}
                  type="text"
                  value={value.legalRepresentative ?? ''}
                  onChange={(e) =>
                    onChange?.(rule.entityCode, { ...value, legalRepresentative: e.target.value })
                  }
                  aria-label={`Representante legal de ${label}`}
                  className="w-full px-3 py-2 rounded-xl border outline-none focus:border-[#557EFF]"
                />
              </div>
            )}

            {asBool(profile, 'requiresRunt') && (
              <p className="text-[10px] opacity-60">Requiere consulta RUNT.</p>
            )}

            {allowsMultiple && (
              <button
                type="button"
                onClick={() => onAddActor?.(rule.entityCode)}
                className="text-[11px] font-bold rounded-lg px-3 py-1.5 border"
                style={{ borderColor: '#557EFF', color: '#557EFF' }}
              >
                + Agregar {label.toLowerCase()}
              </button>
            )}
          </fieldset>
        );
      })}
    </section>
  );
}
