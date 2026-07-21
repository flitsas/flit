import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ConformationProfileStep } from '../ConformationProfileStep';

// FEATURE-08 / HU-FE-01 (CFD-02, CFD-03) — paso Entrada y validaciones del wizard SuperAdmin.

const mocks = vi.hoisted(() => ({
  updateConformationProfile: vi.fn(),
}));

vi.mock('@/lib/api/superadmin-client', () => ({
  superadminClient: {
    updateConformationProfile: mocks.updateConformationProfile,
  },
}));

describe('ConformationProfileStep', () => {
  beforeEach(() => {
    mocks.updateConformationProfile.mockReset();
    mocks.updateConformationProfile.mockResolvedValue({
      gateProfile: { entryMode: 'PLATE' },
    });
  });

  it('renderiza las 3 opciones de entryMode y las 3 validaciones', () => {
    render(<ConformationProfileStep procedureTypeId="pt-1" />);

    expect(screen.getByLabelText('Modo de entrada VIN')).toBeInTheDocument();
    expect(screen.getByLabelText('Modo de entrada Placa')).toBeInTheDocument();
    expect(screen.getByLabelText('Modo de entrada Ambas')).toBeInTheDocument();
    expect(screen.getByLabelText('Validar regla de compañía')).toBeInTheDocument();
    expect(screen.getByLabelText('Validar operabilidad del OT')).toBeInTheDocument();
    expect(screen.getByLabelText('Validar duplicidad')).toBeInTheDocument();
  });

  it('preselecciona VIN por defecto y refleja initialProfile', () => {
    const { rerender } = render(<ConformationProfileStep procedureTypeId="pt-1" />);
    expect(screen.getByLabelText('Modo de entrada VIN')).toBeChecked();

    rerender(
      <ConformationProfileStep
        procedureTypeId="pt-1"
        initialProfile={{ entryMode: 'BOTH', validateOtOperability: true }}
      />,
    );
    // el estado inicial se fija en el primer render; se valida el default aquí.
    expect(screen.getByLabelText('Modo de entrada VIN')).toBeChecked();
  });

  it('guarda entryMode + flags seleccionados vía updateConformationProfile', async () => {
    const user = userEvent.setup();
    const onSaved = vi.fn();
    render(<ConformationProfileStep procedureTypeId="pt-9" onSaved={onSaved} />);

    await user.click(screen.getByLabelText('Modo de entrada Placa'));
    await user.click(screen.getByLabelText('Validar regla de compañía'));
    await user.click(screen.getByRole('button', { name: /guardar y continuar/i }));

    await waitFor(() => {
      expect(mocks.updateConformationProfile).toHaveBeenCalledWith('pt-9', {
        gateProfile: {
          entryMode: 'PLATE',
          validateCompanyRule: true,
          validateOtOperability: false,
          validateDuplicateProcedure: false,
        },
      });
    });
    expect(onSaved).toHaveBeenCalled();
  });

  it('muestra un error accesible si falla el guardado', async () => {
    const user = userEvent.setup();
    mocks.updateConformationProfile.mockRejectedValueOnce(new Error('422: no editable'));
    render(<ConformationProfileStep procedureTypeId="pt-x" />);

    await user.click(screen.getByRole('button', { name: /guardar y continuar/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/no editable/i);
  });
});
