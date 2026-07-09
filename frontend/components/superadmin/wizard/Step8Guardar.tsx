import type { WizardIdentity } from '@/hooks/useParametrizationWizard';
import type { ConformationRuleItem, ProcedureStep } from '@/lib/api/types/procedure-parametrization';

interface Step8Props {
  identity: WizardIdentity;
  rules: ConformationRuleItem[];
  steps: ProcedureStep[];
  validated: boolean;
}

const FAMILY_LABELS: Record<string, string> = {
  MATRICULAS: 'Matrículas',
  TRASPASO: 'Traspaso',
  OTROS: 'Otros Trámites',
};

export function Step8Guardar({ identity, rules, steps, validated }: Step8Props) {
  const activeRules = rules.filter((r) => r.isActive);

  return (
    <div className="space-y-5">
      <div>
        <h2 className="text-base font-bold mb-1">Resumen y guardar</h2>
        <p className="text-xs opacity-60">
          Revisa la configuración antes de guardar como borrador.
        </p>
      </div>

      <div className="space-y-3">
        <div
          className="rounded-2xl p-4 border bg-white dark:bg-[#0B0F14]"
        >
          <p className="text-[10px] font-semibold uppercase opacity-50 mb-2">Identidad</p>
          <div className="grid grid-cols-3 gap-3">
            <div>
              <p className="text-[10px] opacity-50">Familia</p>
              <p className="text-xs font-semibold">{FAMILY_LABELS[identity.family]}</p>
            </div>
            <div>
              <p className="text-[10px] opacity-50">Código</p>
              <p className="text-xs font-mono font-bold">{identity.code || '—'}</p>
            </div>
            <div>
              <p className="text-[10px] opacity-50">Nombre</p>
              <p className="text-xs font-medium">{identity.name || '—'}</p>
            </div>
          </div>
        </div>

        <div
          className="rounded-2xl p-4 border bg-white dark:bg-[#0B0F14]"
        >
          <p className="text-[10px] font-semibold uppercase opacity-50 mb-2">
            Aristas activas ({activeRules.length})
          </p>
          {activeRules.length === 0 ? (
            <p className="text-xs opacity-50">Ninguna arista activa</p>
          ) : (
            <div className="flex flex-wrap gap-2">
              {activeRules.map((r) => (
                <span
                  key={r.procedureEntityCode}
                  className="px-2 py-0.5 rounded-full text-[10px] font-semibold text-white"
                  style={{ background: '#557EFF' }}
                >
                  {r.procedureEntityCode}
                </span>
              ))}
            </div>
          )}
        </div>

        <div
          className="rounded-2xl p-4 border bg-white dark:bg-[#0B0F14]"
        >
          <p className="text-[10px] font-semibold uppercase opacity-50 mb-2">
            Pasos ({steps.length})
          </p>
          {steps.length === 0 ? (
            <p className="text-xs opacity-50">Sin pasos configurados</p>
          ) : (
            <ol className="space-y-1">
              {steps.map((step, i) => (
                <li key={i} className="flex items-center gap-2 text-xs">
                  <span
                    className="h-5 w-5 rounded-full grid place-items-center text-[9px] font-bold text-white shrink-0"
                    style={{ background: '#557EFF' }}
                    aria-hidden="true"
                  >
                    {i + 1}
                  </span>
                  {step.title}
                </li>
              ))}
            </ol>
          )}
        </div>

        <div
          className="flex items-center gap-3 rounded-xl p-3 border text-xs"
          style={{
            borderColor: validated ? '#00DBD5' : '#DFE5ED',
            background: validated ? 'rgba(0,219,213,0.06)' : 'transparent',
            color: validated ? '#00DBD5' : undefined,
          }}
        >
          <svg
            className="h-4 w-4 shrink-0"
            fill="none"
            stroke="currentColor"
            strokeWidth={2}
            viewBox="0 0 24 24"
            aria-hidden="true"
          >
            {validated ? (
              <path strokeLinecap="round" strokeLinejoin="round" d="m4.5 12.75 6 6 9-13.5" />
            ) : (
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                d="M12 9v3.75m9-.75a9 9 0 1 1-18 0 9 9 0 0 1 18 0Zm-9 3.75h.008v.008H12v-.008Z"
              />
            )}
          </svg>
          <span>
            {validated
              ? 'Validación exitosa — listo para publicar'
              : 'Sin validación — se guardará como borrador'}
          </span>
        </div>
      </div>
    </div>
  );
}
