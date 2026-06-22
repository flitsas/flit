import type {
  ConformationRuleItem,
  ProcedureEntityCode,
} from '@/lib/api/types/procedure-parametrization';

interface Step3Props {
  rules: ConformationRuleItem[];
  onToggle: (code: ProcedureEntityCode) => void;
}

const ENTITY_LABELS: Record<ProcedureEntityCode, { label: string; description: string }> = {
  VEHICLE: { label: 'Vehículo', description: 'Datos e historial del vehículo' },
  OWNER: { label: 'Propietario', description: 'Titular actual del bien' },
  BUYER: { label: 'Comprador', description: 'Nuevo adquirente del vehículo' },
  LESSEE: { label: 'Arrendatario', description: 'Aplica en leasing o arrendamiento' },
};

export function Step3Aristas({ rules, onToggle }: Step3Props) {
  return (
    <div className="space-y-5">
      <div>
        <h2 className="text-base font-bold mb-1">Matriz de conformación</h2>
        <p className="text-xs opacity-60">
          Selecciona las aristas (entidades) que participan en este trámite.
        </p>
      </div>

      <div className="space-y-2">
        {rules.map((rule) => {
          const meta = ENTITY_LABELS[rule.procedureEntityCode];
          return (
            <label
              key={rule.procedureEntityCode}
              htmlFor={`arista-${rule.procedureEntityCode}`}
              className="flex items-center gap-4 rounded-xl p-4 border cursor-pointer transition"
              style={{
                borderColor: rule.isActive ? '#557EFF' : '#DFE5ED',
                background: rule.isActive ? 'rgba(85,126,255,0.06)' : 'transparent',
              }}
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
          );
        })}
      </div>

      <p className="text-[10px] opacity-50">
        Mínimo una arista activa requerida para publicar.
      </p>
    </div>
  );
}
