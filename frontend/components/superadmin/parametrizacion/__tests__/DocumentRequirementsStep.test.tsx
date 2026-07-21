import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { DocumentRequirementsStep } from '../DocumentRequirementsStep';

// FEATURE-08 / HU-FE-03 (CFD-06) — paso Documentos del wizard SuperAdmin.

const mocks = vi.hoisted(() => ({ updateConformationProfile: vi.fn() }));
vi.mock('@/lib/api/superadmin-client', () => ({
  superadminClient: { updateConformationProfile: mocks.updateConformationProfile },
}));

describe('DocumentRequirementsStep', () => {
  beforeEach(() => {
    mocks.updateConformationProfile.mockReset();
    mocks.updateConformationProfile.mockResolvedValue({ documentRequirements: [] });
  });

  it('permite agregar documentos al tipo (AC-01)', async () => {
    const user = userEvent.setup();
    render(<DocumentRequirementsStep procedureTypeId="pt-1" />);

    await user.type(screen.getByLabelText('Código de documento'), 'cedula');
    await user.click(screen.getByRole('button', { name: /^agregar$/i }));

    expect(screen.getByTestId('doc-CEDULA')).toBeInTheDocument();
  });

  it('guarda los documentRequirements (obligatorio + buzón)', async () => {
    const user = userEvent.setup();
    render(<DocumentRequirementsStep procedureTypeId="pt-2" />);

    await user.type(screen.getByLabelText('Código de documento'), 'promesa');
    await user.click(screen.getByRole('button', { name: /^agregar$/i }));
    await user.click(screen.getByLabelText('PROMESA buzón'));
    await user.click(screen.getByRole('button', { name: /guardar y continuar/i }));

    await waitFor(() => {
      expect(mocks.updateConformationProfile).toHaveBeenCalledWith('pt-2', {
        documentRequirements: [{ documentTypeCode: 'PROMESA', isRequired: true, isDummy: true }],
      });
    });
  });
});
