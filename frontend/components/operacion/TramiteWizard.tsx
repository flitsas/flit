'use client';

import { useEffect, useMemo } from 'react';
import { Check, ChevronLeft, ChevronRight } from 'lucide-react';
import { useProcedureInstance, buildFieldValueInput } from '@/hooks/useProcedureInstance';
import { DynamicFieldRenderer } from './DynamicFieldRenderer';
import { PreflightPanel } from './PreflightPanel';
import type { ProcedureConfiguration } from '@/lib/api/types/procedure-runtime';
import type { ProcedureStep } from '@/lib/api/types/procedure-parametrization';

interface Props {
  configuration: ProcedureConfiguration;
  procedureTypeId: string;
  onExit: () => void;
}

/** Ordena steps/sections/fields por sortOrder y descarta steps inactivos. */
function orderedSteps(config: ProcedureConfiguration): ProcedureStep[] {
  return [...config.steps]
    .filter((s) => s.isActive !== false)
    .sort((a, b) => a.sortOrder - b.sortOrder)
    .map((s) => ({
      ...s,
      sections: [...(s.sections ?? [])]
        .sort((a, b) => a.sortOrder - b.sortOrder)
        .map((sec) => ({
          ...sec,
          formFields: [...(sec.formFields ?? [])].sort(
            (a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0),
          ),
        })),
    }));
}

export function TramiteWizard({ configuration, procedureTypeId, onExit }: Props) {
  const steps = useMemo(() => orderedSteps(configuration), [configuration]);
  const {
    state,
    setFieldValue,
    setRiesgoAceptado,
    clearError,
    start,
    saveDraft,
    goToStep,
    submit,
    runConsulta,
  } = useProcedureInstance();

  // Crea la instancia draft al montar el wizard.
  useEffect(() => {
    void start(procedureTypeId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [procedureTypeId]);

  const currentStep = steps[state.step];
  const isLast = state.step === steps.length - 1;
  const isFirst = state.step === 0;
  const blockedByRisk =
    state.preflight?.overall === 'red' && !state.riesgoAceptado;

  const buildItemsForStep = (step: ProcedureStep) =>
    step.sections.flatMap((sec) =>
      sec.formFields.map((f) =>
        buildFieldValueInput(f, state.fieldValues[f.fieldKey]),
      ),
    );

  const handleContinue = async () => {
    if (currentStep) {
      await saveDraft(buildItemsForStep(currentStep));
    }
    goToStep(state.step + 1);
  };

  const handleFinish = async () => {
    if (currentStep) {
      await saveDraft(buildItemsForStep(currentStep));
    }
    await submit();
  };

  if (state.submitted) {
    return (
      <div className="h-full w-full grid place-items-center px-6 pb-24">
        <div
          className="max-w-md w-full rounded-3xl p-8 bg-white dark:bg-[#0B0F14] border text-center"
          style={{ borderColor: '#DFE5ED' }}
          role="status"
          aria-live="polite"
        >
          <div
            className="h-16 w-16 mx-auto rounded-full grid place-items-center mb-4"
            style={{ background: 'rgba(0,219,213,0.15)' }}
          >
            <Check className="h-8 w-8" style={{ color: '#00DBD5' }} aria-hidden="true" />
          </div>
          <h2 className="text-xl font-bold">¡Trámite enviado!</h2>
          <p className="text-xs opacity-70 mt-2">
            El trámite {configuration.name} fue radicado correctamente.
          </p>
          <button
            onClick={onExit}
            className="w-full mt-5 py-2.5 rounded-xl text-sm font-semibold text-white"
            style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
          >
            Volver a Operación
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="flex-1 min-h-0 flex flex-col gap-4 overflow-hidden">
      <div className="flex items-center justify-between shrink-0">
        <h1 className="text-xl font-bold">{configuration.name}</h1>
        <button
          onClick={onExit}
          className="text-xs opacity-70 hover:opacity-100"
          aria-label="Cancelar y volver al selector"
        >
          ← Cancelar
        </button>
      </div>

      {state.error && (
        <div
          className="rounded-xl p-3 text-xs border shrink-0 flex items-center justify-between gap-3"
          style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          <span>{state.error}</span>
          <button onClick={clearError} className="font-bold" aria-label="Descartar error">
            ×
          </button>
        </div>
      )}

      <div className="grid grid-cols-12 gap-4 flex-1 min-h-0">
        {/* Sidebar de progreso (patrón FlitWizardSidebar). */}
        <aside
          className="col-span-12 md:col-span-3 rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border"
          style={{ borderColor: '#DFE5ED' }}
        >
          <p className="text-[10px] font-semibold uppercase opacity-60 mb-3">
            Asistente de seguimiento
          </p>
          <ol className="space-y-3">
            {steps.map((s, i) => {
              const isDone = i < state.step;
              const isActive = i === state.step;
              return (
                <li
                  key={s.id ?? s.code}
                  className="flex items-center gap-3"
                  aria-current={isActive ? 'step' : undefined}
                >
                  <span
                    className="h-8 w-8 rounded-full grid place-items-center text-[11px] font-bold shrink-0"
                    style={{
                      background: isDone ? '#8CC63F' : isActive ? '#557EFF' : '#DFE5ED',
                      color: isDone || isActive ? '#fff' : '#162744',
                    }}
                  >
                    {isDone ? <Check className="h-4 w-4" aria-hidden="true" /> : i + 1}
                  </span>
                  <span className={`text-xs ${isActive ? 'font-bold' : 'opacity-70'}`}>
                    {s.title}
                  </span>
                </li>
              );
            })}
          </ol>
        </aside>

        {/* Contenido dinámico del step. */}
        <section
          className="col-span-12 md:col-span-9 rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border overflow-y-auto"
          style={{ borderColor: '#DFE5ED' }}
        >
          {!currentStep ? (
            <p className="text-xs opacity-60">Este flujo no tiene pasos configurados.</p>
          ) : (
            <div className="space-y-6">
              <h2 className="text-base font-bold">{currentStep.title}</h2>
              {currentStep.sections.map((sec) => (
                <div key={sec.id ?? sec.code} className="space-y-3">
                  {currentStep.sections.length > 1 && (
                    <h3 className="text-sm font-semibold opacity-80">{sec.title}</h3>
                  )}
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                    {sec.formFields.map((field) => (
                      <DynamicFieldRenderer
                        key={field.id ?? field.fieldKey}
                        field={field}
                        value={state.fieldValues[field.fieldKey]}
                        onChange={(v) => setFieldValue(field.fieldKey, v)}
                      />
                    ))}
                  </div>
                </div>
              ))}

              {/* Semáforo de consulta en el primer step (AC3). */}
              {isFirst && (
                <PreflightPanel
                  snapshot={state.preflight}
                  loading={state.preflightLoading}
                  onRun={() => void runConsulta()}
                  riesgoAceptado={state.riesgoAceptado}
                  onToggleRiesgo={setRiesgoAceptado}
                />
              )}
            </div>
          )}

          <div
            className="flex items-center justify-between mt-6 pt-4 border-t"
            style={{ borderColor: '#DFE5ED' }}
          >
            <button
              onClick={() => goToStep(Math.max(0, state.step - 1))}
              disabled={isFirst || state.loading}
              className="flex items-center gap-1 px-4 py-2 rounded-xl text-xs font-medium border disabled:opacity-30"
              style={{ borderColor: '#162744', color: '#162744' }}
            >
              <ChevronLeft className="h-3 w-3" /> Anterior
            </button>
            {!isLast ? (
              <button
                onClick={() => void handleContinue()}
                disabled={state.loading || blockedByRisk}
                className="flex items-center gap-1 px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
                style={{ background: '#557EFF' }}
              >
                {state.loading ? 'Guardando…' : 'Continuar'}
                <ChevronRight className="h-3 w-3" />
              </button>
            ) : (
              <button
                onClick={() => void handleFinish()}
                disabled={state.loading || blockedByRisk}
                className="px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50"
                style={{ background: 'linear-gradient(135deg,#557EFF,#00DBD5)' }}
              >
                {state.loading ? 'Enviando…' : 'Finalizar'}
              </button>
            )}
          </div>
        </section>
      </div>
    </div>
  );
}
