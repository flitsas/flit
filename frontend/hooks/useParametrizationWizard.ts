'use client';

import { useCallback, useState } from 'react';
import { superadminClient } from '@/lib/api/superadmin-client';
import {
  buildStepInputsForSave,
  findVehicleSection,
  mapStepsFromApi,
} from '@/lib/api/procedure-parametrization-mappers';
import type {
  ProcedureFamily,
  ConformationRuleItem,
  ProcedureEntityCode,
  ProcedureStep,
  ValidationResult,
} from '@/lib/api/types/procedure-parametrization';

export interface WizardIdentity {
  code: string;
  name: string;
  family: ProcedureFamily;
}

export interface WizardState {
  step: number;
  procedureTypeId: string | null;
  identity: WizardIdentity;
  conformationRules: ConformationRuleItem[];
  steps: ProcedureStep[];
  validationResult: ValidationResult | null;
  loading: boolean;
  error: string | null;
  templateApplied: boolean;
}

const INITIAL_RULES: ConformationRuleItem[] = [
  { procedureEntityCode: 'VEHICLE', isActive: false, sortOrder: 1 },
  { procedureEntityCode: 'OWNER', isActive: false, sortOrder: 2 },
  { procedureEntityCode: 'BUYER', isActive: false, sortOrder: 3 },
  { procedureEntityCode: 'LESSEE', isActive: false, sortOrder: 4 },
];

const DEFAULT_STEPS: ProcedureStep[] = [
  {
    code: 'formulario_base',
    title: 'Formulario base',
    sortOrder: 1,
    isActive: true,
    sections: [],
  },
  {
    code: 'validacion_datos',
    title: 'Validación datos',
    sortOrder: 2,
    isActive: true,
    sections: [],
  },
  {
    code: 'paso_documental',
    title: 'Paso documental',
    sortOrder: 3,
    isActive: true,
    sections: [],
  },
];

export function useParametrizationWizard(editingId?: string | null) {
  const [state, setState] = useState<WizardState>({
    step: 0,
    procedureTypeId: editingId ?? null,
    identity: { code: '', name: '', family: 'MATRICULAS' },
    conformationRules: INITIAL_RULES,
    steps: DEFAULT_STEPS,
    validationResult: null,
    loading: false,
    error: null,
    templateApplied: false,
  });

  const vehicleActive = state.conformationRules.some(
    (r) => r.procedureEntityCode === 'VEHICLE' && r.isActive,
  );

  const setStep = useCallback((step: number) => {
    setState((s) => ({ ...s, step }));
  }, []);

  const setIdentity = useCallback((identity: Partial<WizardIdentity>) => {
    setState((s) => ({ ...s, identity: { ...s.identity, ...identity } }));
  }, []);

  const toggleRule = useCallback((code: ProcedureEntityCode) => {
    setState((s) => ({
      ...s,
      conformationRules: s.conformationRules.map((r) =>
        r.procedureEntityCode === code ? { ...r, isActive: !r.isActive } : r,
      ),
    }));
  }, []);

  // FEATURE-08 / CFD-05 — banderas por-actor del validation_profile de la regla (PN/PJ, multiplicidad,
  // RUNT). Se persisten con updateConformationRules (ya envía validationProfile en cada regla).
  const setRuleProfile = useCallback(
    (code: ProcedureEntityCode, patch: Record<string, boolean>) => {
      setState((s) => ({
        ...s,
        conformationRules: s.conformationRules.map((r) =>
          r.procedureEntityCode === code
            ? { ...r, validationProfile: { ...(r.validationProfile ?? {}), ...patch } }
            : r,
        ),
      }));
    },
    [],
  );

  const moveStep = useCallback((index: number, direction: 'up' | 'down') => {
    setState((s) => {
      const arr = [...s.steps];
      const target = direction === 'up' ? index - 1 : index + 1;
      if (target < 0 || target >= arr.length) return s;
      [arr[index], arr[target]] = [arr[target], arr[index]];
      return {
        ...s,
        steps: arr.map((stepItem, i) => ({ ...stepItem, sortOrder: i + 1 })),
      };
    });
  }, []);

  const addStep = useCallback((title: string) => {
    setState((s) => {
      const index = s.steps.length;
      return {
        ...s,
        steps: [
          ...s.steps,
          {
            code: `step_${index + 1}`,
            title,
            sortOrder: index + 1,
            isActive: true,
            sections: [],
          },
        ],
      };
    });
  }, []);

  const removeStep = useCallback((index: number) => {
    setState((s) => {
      const arr = s.steps.filter((_, i) => i !== index);
      return {
        ...s,
        steps: arr.map((stepItem, i) => ({ ...stepItem, sortOrder: i + 1 })),
      };
    });
  }, []);

  const refreshSteps = useCallback(async (procedureTypeId: string) => {
    const raw = await superadminClient.getSteps(procedureTypeId);
    const steps = mapStepsFromApi(raw);
    setState((s) => ({ ...s, steps }));
    return steps;
  }, []);

  const saveIdentityAndProceed = useCallback(async () => {
    setState((s) => ({ ...s, loading: true, error: null }));
    try {
      let id = state.procedureTypeId;
      if (!id) {
        const created = await superadminClient.createProcedureType(state.identity);
        id = created.id;
      }
      setState((s) => ({ ...s, procedureTypeId: id, loading: false, step: s.step + 1 }));
    } catch (err) {
      setState((s) => ({
        ...s,
        loading: false,
        error: err instanceof Error ? err.message : 'Error al guardar identidad',
      }));
    }
  }, [state.procedureTypeId, state.identity]);

  const saveConformationAndProceed = useCallback(async () => {
    if (!state.procedureTypeId) return;
    setState((s) => ({ ...s, loading: true, error: null }));
    try {
      await superadminClient.updateConformationRules(
        state.procedureTypeId,
        state.conformationRules,
      );
      setState((s) => ({ ...s, loading: false, step: s.step + 1 }));
    } catch (err) {
      setState((s) => ({
        ...s,
        loading: false,
        error: err instanceof Error ? err.message : 'Error al guardar aristas',
      }));
    }
  }, [state.procedureTypeId, state.conformationRules]);

  const saveStepsAndProceed = useCallback(async () => {
    if (!state.procedureTypeId) return;
    setState((s) => ({ ...s, loading: true, error: null }));
    try {
      const payload = buildStepInputsForSave(state.steps, state.conformationRules);
      const saved = await superadminClient.updateSteps(state.procedureTypeId, payload);
      const steps = mapStepsFromApi(saved);
      setState((s) => ({ ...s, steps, loading: false, step: s.step + 1 }));
    } catch (err) {
      setState((s) => ({
        ...s,
        loading: false,
        error: err instanceof Error ? err.message : 'Error al guardar pasos',
      }));
    }
  }, [state.procedureTypeId, state.steps, state.conformationRules]);

  const applyVehicleTemplate = useCallback(async () => {
    if (!state.procedureTypeId) return;
    setState((s) => ({ ...s, loading: true, error: null }));
    try {
      const steps = await refreshSteps(state.procedureTypeId);
      const templates = await superadminClient.listConsultationTemplates();
      const runtVehicle = templates.find((t) => t.code === 'RUNT_VEHICLE' && t.isActive);
      if (!runtVehicle) {
        throw new Error('Plantilla RUNT_VEHICLE no encontrada en catálogo');
      }

      const section = findVehicleSection(steps);
      if (!section?.id) {
        throw new Error('No hay sección de vehículo configurada. Guarda los pasos primero.');
      }

      await superadminClient.applyTemplateFields(runtVehicle.id, {
        procedureTypeId: state.procedureTypeId,
        sectionId: section.id,
      });

      await refreshSteps(state.procedureTypeId);
      setState((s) => ({ ...s, loading: false, templateApplied: true }));
    } catch (err) {
      setState((s) => ({
        ...s,
        loading: false,
        error: err instanceof Error ? err.message : 'Error al aplicar plantilla',
      }));
    }
  }, [state.procedureTypeId, refreshSteps]);

  const saveCamposAndProceed = useCallback(async () => {
    if (!state.procedureTypeId) return;
    setState((s) => ({ ...s, loading: true, error: null }));
    try {
      await refreshSteps(state.procedureTypeId);
      setState((s) => ({ ...s, loading: false, step: s.step + 1 }));
    } catch (err) {
      setState((s) => ({
        ...s,
        loading: false,
        error: err instanceof Error ? err.message : 'Error al cargar campos',
      }));
    }
  }, [state.procedureTypeId, refreshSteps]);

  const runValidation = useCallback(async () => {
    if (!state.procedureTypeId) return;
    setState((s) => ({ ...s, loading: true, error: null, validationResult: null }));
    try {
      const result = await superadminClient.validate(state.procedureTypeId);
      setState((s) => ({ ...s, loading: false, validationResult: result }));
    } catch (err) {
      setState((s) => ({
        ...s,
        loading: false,
        error: err instanceof Error ? err.message : 'Error al validar',
      }));
    }
  }, [state.procedureTypeId]);

  const clearError = useCallback(() => {
    setState((s) => ({ ...s, error: null }));
  }, []);

  return {
    state,
    vehicleActive,
    setStep,
    setIdentity,
    toggleRule,
    setRuleProfile,
    moveStep,
    addStep,
    removeStep,
    saveIdentityAndProceed,
    saveConformationAndProceed,
    saveStepsAndProceed,
    applyVehicleTemplate,
    saveCamposAndProceed,
    runValidation,
    clearError,
  };
}
