'use client';

import { Check, ChevronLeft, ChevronRight } from 'lucide-react';
import { useParametrizationWizard } from '@/hooks/useParametrizationWizard';
import { Step1Identidad } from './wizard/Step1Identidad';
import { Step2Tipologia } from './wizard/Step2Tipologia';
import { Step3Aristas } from './wizard/Step3Aristas';
import { Step4Pasos } from './wizard/Step4Pasos';
import { Step5Campos } from './wizard/Step5Campos';
import { Step6Bindings } from './wizard/Step6Bindings';
import { Step7Validar } from './wizard/Step7Validar';
import { Step8Guardar } from './wizard/Step8Guardar';

const WIZARD_STEPS = [
  'Identidad',
  'Tipología',
  'Aristas',
  'Pasos',
  'Campos',
  'Bindings',
  'Validar',
  'Guardar',
];

interface ParametrizationWizardProps {
  editingId?: string | null;
  onExit: (saved?: boolean) => void;
}

export function ParametrizationWizard({ editingId, onExit }: ParametrizationWizardProps) {
  const {
    state,
    vehicleActive,
    setStep,
    setIdentity,
    toggleRule,
    moveStep,
    addStep,
    removeStep,
    saveIdentityAndProceed,
    saveConformationAndProceed,
    saveStepsAndProceed,
    applyVehicleTemplate,
    saveCamposAndProceed,
    runValidation,
  } = useParametrizationWizard(editingId);

  const {
    step,
    identity,
    conformationRules,
    steps,
    validationResult,
    loading,
    error,
    templateApplied,
  } = state;

  const canContinue = () => {
    if (step === 0) return identity.code.trim() !== '' && identity.name.trim() !== '';
    return true;
  };

  const handleContinue = async () => {
    if (step === 0) {
      await saveIdentityAndProceed();
      return;
    }
    if (step === 2) {
      await saveConformationAndProceed();
      return;
    }
    if (step === 3) {
      await saveStepsAndProceed();
      return;
    }
    if (step === 4) {
      await saveCamposAndProceed();
      return;
    }
    if (step === 6) {
      return;
    }
    setStep(step + 1);
  };

  const handleBack = () => {
    if (step > 0) setStep(step - 1);
  };

  const handleFinish = () => {
    onExit(true);
  };

  const isLastStep = step === WIZARD_STEPS.length - 1;
  const isValidateStep = step === 6;

  return (
    <div className="h-full w-full px-6 pt-5 pb-24 flex flex-col gap-4 overflow-hidden">
      <div className="flex items-center justify-between shrink-0">
        <h1 className="text-xl font-bold">
          {editingId ? 'Editar parametrización' : 'Nuevo flujo de parametrización'}
        </h1>
        <button
          onClick={() => onExit(false)}
          className="text-xs opacity-70 hover:opacity-100"
          aria-label="Cancelar y volver al listado"
        >
          ← Cancelar
        </button>
      </div>

      <div className="grid grid-cols-12 gap-4 flex-1 min-h-0">
        <aside
          className="col-span-12 md:col-span-3 rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border shrink-0 md:h-fit"
          style={{ borderColor: '#DFE5ED' }}
          aria-label="Pasos del asistente"
        >
          <p className="text-[10px] font-semibold uppercase opacity-60 mb-3">
            Asistente de parametrización
          </p>
          <ol className="space-y-3">
            {WIZARD_STEPS.map((s, i) => {
              const isDone = i < step;
              const isActive = i === step;
              return (
                <li key={s} className="flex items-center gap-3">
                  <span
                    className="h-8 w-8 rounded-full grid place-items-center text-[11px] font-bold shrink-0"
                    style={{
                      background: isDone ? '#00DBD5' : isActive ? '#557EFF' : '#DFE5ED',
                      color: isDone || isActive ? '#fff' : '#162744',
                    }}
                    aria-current={isActive ? 'step' : undefined}
                  >
                    {isDone ? <Check className="h-4 w-4" aria-hidden="true" /> : i + 1}
                  </span>
                  <span className={`text-xs ${isActive ? 'font-bold' : 'opacity-70'}`}>
                    {s}
                  </span>
                </li>
              );
            })}
          </ol>
        </aside>

        <section
          className="col-span-12 md:col-span-9 rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border overflow-y-auto"
          style={{ borderColor: '#DFE5ED' }}
          aria-label={`Paso ${step + 1}: ${WIZARD_STEPS[step]}`}
        >
          {error && (
            <div
              className="mb-4 rounded-xl p-3 border text-xs"
              style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
              role="alert"
              aria-live="polite"
            >
              {error}
            </div>
          )}

          {step === 0 && (
            <Step1Identidad identity={identity} onChange={setIdentity} />
          )}
          {step === 1 && <Step2Tipologia />}
          {step === 2 && (
            <Step3Aristas rules={conformationRules} onToggle={toggleRule} />
          )}
          {step === 3 && (
            <Step4Pasos
              steps={steps}
              onMove={moveStep}
              onAdd={addStep}
              onRemove={removeStep}
            />
          )}
          {step === 4 && (
            <Step5Campos
              steps={steps}
              vehicleActive={vehicleActive}
              loading={loading}
              templateApplied={templateApplied}
              onApplyVehicleTemplate={applyVehicleTemplate}
            />
          )}
          {step === 5 && <Step6Bindings />}
          {step === 6 && (
            <Step7Validar
              validationResult={validationResult}
              loading={loading}
              error={null}
              onValidate={runValidation}
            />
          )}
          {step === 7 && (
            <Step8Guardar
              identity={identity}
              rules={conformationRules}
              steps={steps}
              validated={validationResult?.isValid ?? false}
            />
          )}

          <div
            className="flex items-center justify-between mt-6 pt-4 border-t"
            style={{ borderColor: '#DFE5ED' }}
          >
            <button
              onClick={handleBack}
              disabled={step === 0 || loading}
              className="flex items-center gap-1 px-4 py-2 rounded-xl text-xs font-medium border disabled:opacity-30"
              style={{ borderColor: '#162744', color: '#162744' }}
              aria-label="Ir al paso anterior"
            >
              <ChevronLeft className="h-3 w-3" aria-hidden="true" />
              Anterior
            </button>

            {isLastStep ? (
              <button
                onClick={handleFinish}
                disabled={loading}
                className="px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
                style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
                aria-label="Guardar borrador y volver al listado"
              >
                Guardar borrador
              </button>
            ) : isValidateStep ? (
              <button
                onClick={() => setStep(step + 1)}
                disabled={loading}
                className="flex items-center gap-1 px-5 py-2 rounded-xl text-xs font-semibold disabled:opacity-50"
                style={{ background: '#557EFF', color: '#fff' }}
                aria-label="Continuar al siguiente paso"
              >
                Continuar
                <ChevronRight className="h-3 w-3" aria-hidden="true" />
              </button>
            ) : (
              <button
                onClick={handleContinue}
                disabled={!canContinue() || loading}
                className="flex items-center gap-1 px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
                style={{ background: '#557EFF' }}
                aria-label="Continuar al siguiente paso"
              >
                {loading ? 'Guardando…' : 'Continuar'}
                <ChevronRight className="h-3 w-3" aria-hidden="true" />
              </button>
            )}
          </div>
        </section>
      </div>
    </div>
  );
}
