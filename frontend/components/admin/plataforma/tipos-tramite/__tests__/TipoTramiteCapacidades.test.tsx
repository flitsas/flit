import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ConformationProfile } from '@/lib/api/types/procedure-parametrization';

const { mocks } = vi.hoisted(() => ({
  mocks: {
    updateConformationProfile: vi.fn(),
  },
}));

vi.mock('@/lib/api/superadmin-client', () => ({
  superadminClient: mocks,
}));

import { TipoTramiteCapacidades } from '../TipoTramiteCapacidades';

const PERFIL: ConformationProfile = {
  procedureTypeId: 'id-traspaso',
  code: 'TRASPASO_STANDARD',
  publicationStatus: 'published',
  version: 4,
  gateProfile: { entryMode: 'PLATE', requiresBuyer: true, requiresSeller: true },
  conformationRules: [],
  sources: [],
  documentRequirements: [],
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.updateConformationProfile.mockResolvedValue(PERFIL);
});

describe('TipoTramiteCapacidades — generación de impronta', () => {
  it('permite elegir si el tipo genera la impronta y lo guarda en gate_profile', async () => {
    const user = userEvent.setup();
    const onGuardado = vi.fn();
    render(<TipoTramiteCapacidades perfil={PERFIL} onGuardado={onGuardado} />);

    await user.selectOptions(screen.getByLabelText('Generación de impronta'), 'MANUAL');
    await user.click(screen.getByRole('button', { name: 'Guardar capacidades' }));

    await waitFor(() =>
      expect(mocks.updateConformationProfile).toHaveBeenCalledWith(
        'id-traspaso',
        expect.objectContaining({
          gateProfile: expect.objectContaining({ improntaSource: 'MANUAL' }),
        }),
      ),
    );
    expect(onGuardado).toHaveBeenCalled();
  });

  it('OPERATOR_CHOICE deja que el gestor genere o cargue', async () => {
    const user = userEvent.setup();
    render(<TipoTramiteCapacidades perfil={PERFIL} onGuardado={vi.fn()} />);

    await user.selectOptions(
      screen.getByLabelText('Generación de impronta'),
      'OPERATOR_CHOICE',
    );
    await user.click(screen.getByRole('button', { name: 'Guardar capacidades' }));

    await waitFor(() =>
      expect(mocks.updateConformationProfile).toHaveBeenCalledWith(
        'id-traspaso',
        expect.objectContaining({
          gateProfile: expect.objectContaining({ improntaSource: 'OPERATOR_CHOICE' }),
        }),
      ),
    );
  });
});
