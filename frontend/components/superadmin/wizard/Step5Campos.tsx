import type { ProcedureStep } from '@/lib/api/types/procedure-parametrization';
import { hasLockedPlateOrVin } from '@/lib/api/procedure-parametrization-mappers';

interface Step5Props {
  steps: ProcedureStep[];
  vehicleActive: boolean;
  loading: boolean;
  templateApplied: boolean;
  onApplyVehicleTemplate: () => void;
}

export function Step5Campos({
  steps,
  vehicleActive,
  loading,
  templateApplied,
  onApplyVehicleTemplate,
}: Step5Props) {
  const plateOrVinReady = hasLockedPlateOrVin(steps);

  return (
    <div className="space-y-5">
      <div>
        <h2 className="text-base font-bold mb-1">Campos por paso</h2>
        <p className="text-xs opacity-60">
          Aplica plantillas de consulta para poblar campos bloqueados requeridos por las aristas
          activas.
        </p>
      </div>

      {vehicleActive && (
        <div
          className="rounded-2xl p-4 border"
          style={{ borderColor: '#557EFF', background: 'rgba(85,126,255,0.06)' }}
        >
          <p className="text-xs font-semibold mb-1">Arista VEHICLE activa</p>
          <p className="text-[11px] opacity-70 mb-3">
            Matrículas con vehículo requieren el campo bloqueado{' '}
            <code className="font-mono text-[10px]">plate_or_vin</code> vía plantilla RUNT.
          </p>
          <button
            type="button"
            onClick={onApplyVehicleTemplate}
            disabled={loading || plateOrVinReady}
            className="px-4 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
            style={{ background: plateOrVinReady ? '#00DBD5' : '#557EFF' }}
            aria-label="Aplicar plantilla RUNT vehículo a la sección de datos del vehículo"
          >
            {loading
              ? 'Aplicando…'
              : plateOrVinReady || templateApplied
                ? 'Plantilla RUNT aplicada'
                : 'Aplicar plantilla RUNT Vehículo'}
          </button>
          {!plateOrVinReady && !templateApplied && (
            <p className="text-[10px] opacity-50 mt-2">
              Sin aplicar la plantilla, la validación reportará VIN_PLATE_RULE.
            </p>
          )}
        </div>
      )}

      {steps.length === 0 ? (
        <p className="text-xs opacity-50 italic">
          Define primero los pasos en el paso anterior.
        </p>
      ) : (
        <div className="space-y-3">
          {steps.map((step, i) => (
            <div
              key={step.id ?? i}
              className="rounded-2xl p-4 border bg-white dark:bg-[#0B0F14]"
              style={{ borderColor: '#DFE5ED' }}
            >
              <div className="flex items-center gap-2 mb-3">
                <span
                  className="h-6 w-6 rounded-full grid place-items-center text-[10px] font-bold text-white shrink-0"
                  style={{ background: '#557EFF' }}
                  aria-hidden="true"
                >
                  {i + 1}
                </span>
                <h3 className="text-xs font-bold">{step.title}</h3>
              </div>

              {step.sections.length === 0 ? (
                <p className="text-[10px] opacity-50 italic">Sin secciones configuradas.</p>
              ) : (
                <div className="space-y-2">
                  {step.sections.map((section, si) => (
                    <div
                      key={section.id ?? si}
                      className="pl-3 border-l-2"
                      style={{ borderColor: '#DFE5ED' }}
                    >
                      <p className="text-[11px] font-semibold mb-1">{section.title}</p>
                      {section.formFields.length === 0 ? (
                        <p className="text-[10px] opacity-40">Sin campos</p>
                      ) : (
                        <ul className="space-y-1">
                          {section.formFields.map((field, fi) => (
                            <li key={field.id ?? fi} className="flex items-center gap-2 text-[10px]">
                              <span className="opacity-70">{field.label}</span>
                              <span className="font-mono opacity-40">({field.fieldKey})</span>
                              {field.isLocked && (
                                <span
                                  className="px-1.5 py-0.5 rounded text-[9px] font-bold text-white"
                                  style={{ background: '#F9AC00' }}
                                  title={field.lockReason ?? 'Campo bloqueado'}
                                >
                                  LOCKED
                                </span>
                              )}
                              {field.isRequired && (
                                <span style={{ color: '#FF4E00' }} aria-hidden="true">
                                  *
                                </span>
                              )}
                            </li>
                          ))}
                        </ul>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
