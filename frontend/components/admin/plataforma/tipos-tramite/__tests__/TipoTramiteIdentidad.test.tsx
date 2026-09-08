import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ProcedureTypeSummary } from '@/lib/api/types/procedure-parametrization';

/**
 * HU #12125 — editar el copy (Description) de un tipo de trámite desde su pestaña Identidad.
 */
const { mocks } = vi.hoisted(() => ({
  mocks: {
    updateProcedureType: vi.fn(),
  },
}));

vi.mock('@/lib/api/superadmin-client', () => ({
  superadminClient: mocks,
}));

import { TipoTramiteIdentidad } from '../TipoTramiteIdentidad';

const BLINDAJE: ProcedureTypeSummary = {
  id: 'id-blindaje',
  code: 'BLINDAJE',
  name: 'Blindaje',
  family: 'OTROS',
  publicationStatus: 'published',
  isActive: true,
  wizardEnabled: false,
  publishedAt: null,
  description: 'Copy actual del trámite.',
};

beforeEach(() => {
  vi.clearAllMocks();
});

describe('TipoTramiteIdentidad — copy del tipo (HU #12125)', () => {
  it('AC1: precarga la descripción vigente y al guardar la persiste', async () => {
    const user = userEvent.setup();
    const onGuardado = vi.fn();
    mocks.updateProcedureType.mockResolvedValue({
      ...BLINDAJE,
      description: 'Nuevo copy que ve el gestor.',
    });

    render(<TipoTramiteIdentidad tipo={BLINDAJE} onGuardado={onGuardado} />);

    const textarea = screen.getByLabelText('Descripción');
    expect(textarea).toHaveValue('Copy actual del trámite.');

    await user.clear(textarea);
    await user.type(textarea, 'Nuevo copy que ve el gestor.');
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() =>
      expect(mocks.updateProcedureType).toHaveBeenCalledWith(
        'id-blindaje',
        expect.objectContaining({ description: 'Nuevo copy que ve el gestor.' }),
      ),
    );
    expect(onGuardado).toHaveBeenCalledWith(
      expect.objectContaining({ description: 'Nuevo copy que ve el gestor.' }),
    );
    expect(await screen.findByText('Guardado')).toBeInTheDocument();
  });

  it('AC2: deja el campo vacío y guarda sin bloquear, enviando description: null', async () => {
    const user = userEvent.setup();
    mocks.updateProcedureType.mockResolvedValue({ ...BLINDAJE, description: null });

    render(<TipoTramiteIdentidad tipo={BLINDAJE} onGuardado={vi.fn()} />);

    const textarea = screen.getByLabelText('Descripción');
    await user.clear(textarea);
    const guardarBtn = screen.getByRole('button', { name: 'Guardar cambios' });
    expect(guardarBtn).not.toBeDisabled();
    await user.click(guardarBtn);

    await waitFor(() =>
      expect(mocks.updateProcedureType).toHaveBeenCalledWith(
        'id-blindaje',
        expect.objectContaining({ description: null }),
      ),
    );
    expect(await screen.findByText('Guardado')).toBeInTheDocument();
  });

  it('un tipo sin descripción todavía arranca con el campo vacío, sin romper', () => {
    render(
      <TipoTramiteIdentidad tipo={{ ...BLINDAJE, description: null }} onGuardado={vi.fn()} />,
    );
    expect(screen.getByLabelText('Descripción')).toHaveValue('');
    expect(screen.getByRole('button', { name: 'Guardar cambios' })).toBeDisabled();
  });
});
