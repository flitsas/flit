import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ParametrizationWizard } from '../ParametrizationWizard';

// FEATURE-08 / HU-FE-05 (AC-06) — el ParametrizationWizard incluye los pasos del configurador.

const hookState = vi.hoisted(() => ({
  state: {
    step: 0,
    identity: { code: '', name: '', family: 'MATRICULAS' },
    conformationRules: [],
    steps: [],
    validationResult: null,
    procedureTypeId: null as string | null,
    loading: false,
    error: null as string | null,
    templateApplied: false,
  },
}));

vi.mock('@/hooks/useParametrizationWizard', () => ({
  useParametrizationWizard: () => ({
    state: hookState.state,
    vehicleActive: false,
    setStep: vi.fn(),
    setIdentity: vi.fn(),
    toggleRule: vi.fn(),
    moveStep: vi.fn(),
    addStep: vi.fn(),
    removeStep: vi.fn(),
    saveIdentityAndProceed: vi.fn(),
    saveConformationAndProceed: vi.fn(),
    saveStepsAndProceed: vi.fn(),
    applyVehicleTemplate: vi.fn(),
    saveCamposAndProceed: vi.fn(),
    runValidation: vi.fn(),
  }),
}));

describe('ParametrizationWizard (AC-06)', () => {
  it('incluye los pasos del configurador dinámico (Entrada, Fuentes, Documentos)', () => {
    render(<ParametrizationWizard onExit={vi.fn()} />);

    const sidebar = screen.getByRole('list');
    expect(sidebar).toHaveTextContent('Entrada');
    expect(sidebar).toHaveTextContent('Fuentes');
    expect(sidebar).toHaveTextContent('Documentos');
    // conserva los pasos originales
    expect(sidebar).toHaveTextContent('Identidad');
    expect(sidebar).toHaveTextContent('Pasos');
    expect(sidebar).toHaveTextContent('Validar');
    expect(sidebar).toHaveTextContent('Guardar');
  });
});
