import type { ProcedureFamily } from '@/lib/api/types/procedure-parametrization';
import type { WizardIdentity } from '@/hooks/useParametrizationWizard';

interface Step1Props {
  identity: WizardIdentity;
  onChange: (patch: Partial<WizardIdentity>) => void;
}

const FAMILIES: { value: ProcedureFamily; label: string }[] = [
  { value: 'MATRICULAS', label: 'Matrículas' },
  { value: 'TRASPASO', label: 'Traspaso' },
  { value: 'OTROS', label: 'Otros Trámites' },
];

export function Step1Identidad({ identity, onChange }: Step1Props) {
  return (
    <div className="space-y-5">
      <div>
        <h2 className="text-base font-bold mb-1">Identidad del flujo</h2>
        <p className="text-xs opacity-60">Define la familia, código único y nombre del trámite.</p>
      </div>

      <div className="space-y-4">
        <div>
          <label htmlFor="pt-family" className="text-xs font-semibold block mb-1.5">
            Familia <span style={{ color: '#FF4E00' }}>*</span>
          </label>
          <select
            id="pt-family"
            required
            value={identity.family}
            onChange={(e) => onChange({ family: e.target.value as ProcedureFamily })}
            className="w-full md:w-1/2 px-3 py-2 rounded-xl border bg-white dark:bg-[#0B0F14] text-xs outline-none focus:border-[#557EFF]"
            style={{ borderColor: '#DFE5ED' }}
          >
            {FAMILIES.map((f) => (
              <option key={f.value} value={f.value}>
                {f.label}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label htmlFor="pt-code" className="text-xs font-semibold block mb-1.5">
            Código único <span style={{ color: '#FF4E00' }}>*</span>
          </label>
          <input
            id="pt-code"
            type="text"
            required
            placeholder="Ej. MI_ESTANDAR"
            value={identity.code}
            onChange={(e) => onChange({ code: e.target.value.toUpperCase().replace(/\s/g, '_') })}
            className="w-full md:w-1/2 px-3 py-2 rounded-xl border bg-white dark:bg-[#0B0F14] text-xs outline-none focus:border-[#557EFF] font-mono tracking-wide"
            style={{ borderColor: '#DFE5ED' }}
          />
          <p className="text-[10px] opacity-50 mt-1">Solo mayúsculas y guiones bajos.</p>
        </div>

        <div>
          <label htmlFor="pt-name" className="text-xs font-semibold block mb-1.5">
            Nombre <span style={{ color: '#FF4E00' }}>*</span>
          </label>
          <input
            id="pt-name"
            type="text"
            required
            placeholder="Ej. Matrícula Estándar"
            value={identity.name}
            onChange={(e) => onChange({ name: e.target.value })}
            className="w-full md:w-2/3 px-3 py-2 rounded-xl border bg-white dark:bg-[#0B0F14] text-xs outline-none focus:border-[#557EFF]"
            style={{ borderColor: '#DFE5ED' }}
          />
        </div>
      </div>
    </div>
  );
}
