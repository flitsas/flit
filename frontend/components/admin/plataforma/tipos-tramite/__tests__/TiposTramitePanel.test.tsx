import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

/**
 * ADR-0050 — configurador de tipos de trámite.
 *
 * Lo que estas pruebas fijan es lo que hacía falta y no existía: poder habilitar un tipo desde una
 * pantalla, ver por qué uno no se puede habilitar todavía, y corregir la parametrización de un tipo
 * PUBLICADO — que son los 21 del catálogo.
 */
// La clase va dentro de `vi.hoisted`: `vi.mock` se iza al principio del archivo y una declaración
// de clase en el cuerpo del módulo todavía no está inicializada cuando la fábrica corre.
const { mocks, ApiError } = vi.hoisted(() => {
  class ApiError extends Error {
    constructor(
      readonly status: number,
      readonly body: unknown,
    ) {
      super('error');
      this.name = 'SuperadminApiError';
    }
  }
  return {
    ApiError,
    mocks: {
      listProcedureTypes: vi.fn(),
      getConformationProfile: vi.fn(),
      getSteps: vi.fn(),
      validate: vi.fn(),
      setWizardEnabled: vi.fn(),
      updateProcedureType: vi.fn(),
    },
  };
});

vi.mock('@/lib/api/superadmin-client', () => ({
  superadminClient: mocks,
  SuperadminApiError: ApiError,
}));

vi.mock('@/lib/api/admin-document-types', () => ({
  fetchDocumentTypes: vi.fn().mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 50 }),
}));

import { TiposTramitePanel } from '../TiposTramitePanel';

const BLINDAJE = {
  id: 'id-blindaje',
  code: 'BLINDAJE',
  name: 'Blindaje',
  family: 'OTROS' as const,
  publicationStatus: 'published' as const,
  isActive: true,
  wizardEnabled: false,
  publishedAt: null,
};

const MATRICULA = { ...BLINDAJE, id: 'id-mat', code: 'MATRICULA_NUEVA', name: 'Matrícula inicial', family: 'MATRICULAS' as const, wizardEnabled: true };

beforeEach(() => {
  vi.clearAllMocks();
  mocks.listProcedureTypes.mockResolvedValue([MATRICULA, BLINDAJE]);
  mocks.getConformationProfile.mockResolvedValue({
    procedureTypeId: 'id-blindaje',
    code: 'BLINDAJE',
    publicationStatus: 'published',
    version: 3,
    gateProfile: { entryMode: 'PLATE', requiresBuyer: true },
    conformationRules: [],
    sources: [],
    documentRequirements: [],
  });
  mocks.getSteps.mockResolvedValue([]);
  mocks.validate.mockResolvedValue({ isValid: true, errors: [] });
});

describe('Configurador de tipos de trámite', () => {
  it('resume cuántos tipos hay y cuántos son operables', async () => {
    render(<TiposTramitePanel />);

    // Es el dato que responde «¿qué puede hacer hoy el gestor?», y no existía en ninguna pantalla.
    expect(await screen.findByText(/2 tipos en el catálogo/)).toBeInTheDocument();
    expect(screen.getByText(/1 operables/)).toBeInTheDocument();
  });

  it('habilita un tipo desde el interruptor', async () => {
    const user = userEvent.setup();
    mocks.setWizardEnabled.mockResolvedValue({ ...BLINDAJE, wizardEnabled: true });
    render(<TiposTramitePanel />);

    await user.click(await screen.findByRole('button', { name: /Blindaje/ }));
    await user.click(await screen.findByRole('switch', { name: /Blindaje/ }));

    await waitFor(() =>
      expect(mocks.setWizardEnabled).toHaveBeenCalledWith('id-blindaje', true),
    );
    expect(await screen.findByText(/el gestor puede elegirlo/)).toBeInTheDocument();
  });

  it('cuando el tipo no está listo, dice TODO lo que le falta', async () => {
    // Enterarse de un impedimento por vez convierte dar de alta un tipo en una sucesión de intentos.
    const user = userEvent.setup();
    mocks.setWizardEnabled.mockRejectedValue(
      new ApiError(422, { motivos: ['El tipo no tiene pasos parametrizados.', 'El tipo está inactivo.'] }),
    );
    render(<TiposTramitePanel />);

    await user.click(await screen.findByRole('button', { name: /Blindaje/ }));
    await user.click(await screen.findByRole('switch', { name: /Blindaje/ }));

    const aviso = await screen.findByRole('alert');
    expect(within(aviso).getByText(/no se puede habilitar/i)).toBeInTheDocument();
    expect(within(aviso).getByText(/no tiene pasos parametrizados/)).toBeInTheDocument();
    expect(within(aviso).getByText(/está inactivo/)).toBeInTheDocument();
  });

  it('avisa de que corregir un tipo publicado sube su versión', async () => {
    const user = userEvent.setup();
    render(<TiposTramitePanel />);

    await user.click(await screen.findByRole('button', { name: /Blindaje/ }));
    const nombre = await screen.findByLabelText('Nombre');
    await user.clear(nombre);
    await user.type(nombre, 'Blindaje de vehículo');

    // El gestor debe saber qué implica guardar antes de pulsar, no después.
    expect(await screen.findByText(/sube su versión/)).toBeInTheDocument();
    expect(screen.getByText(/Los trámites en curso no cambian/)).toBeInTheDocument();
  });

  it('permite reclasificar la familia, que es la corrección que negocio pide', async () => {
    const user = userEvent.setup();
    mocks.updateProcedureType.mockResolvedValue({ ...BLINDAJE, family: 'MATRICULAS' });
    render(<TiposTramitePanel />);

    await user.click(await screen.findByRole('button', { name: /Blindaje/ }));
    await user.selectOptions(await screen.findByLabelText('Familia'), 'MATRICULAS');
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() =>
      expect(mocks.updateProcedureType).toHaveBeenCalledWith(
        'id-blindaje',
        expect.objectContaining({ family: 'MATRICULAS' }),
      ),
    );
  });

  it('muestra los problemas de validación sin que haya que pulsar nada', async () => {
    const user = userEvent.setup();
    mocks.validate.mockResolvedValue({
      isValid: false,
      errors: [{ code: 'GATE_PROFILE_BIOMETRIC_ACTORS_MISSING', message: 'requiresBiometrics exige al menos un actor.', path: 'x' }],
    });
    render(<TiposTramitePanel />);

    await user.click(await screen.findByRole('button', { name: /Blindaje/ }));

    expect(await screen.findByText(/exige al menos un actor/)).toBeInTheDocument();
  });
});
