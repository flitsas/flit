import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { SourcesStep } from '../SourcesStep';

// FEATURE-08 / HU-FE-02 (CFD-04) — paso Fuentes del wizard SuperAdmin.

const mocks = vi.hoisted(() => ({ updateConformationProfile: vi.fn() }));

vi.mock('@/lib/api/superadmin-client', () => ({
  superadminClient: { updateConformationProfile: mocks.updateConformationProfile },
}));

describe('SourcesStep', () => {
  beforeEach(() => {
    mocks.updateConformationProfile.mockReset();
    mocks.updateConformationProfile.mockResolvedValue({ sources: [] });
  });

  it('renderiza el catálogo de fuentes', () => {
    render(<SourcesStep procedureTypeId="pt-1" />);
    expect(screen.getByLabelText('Fuente RUNT')).toBeInTheDocument();
    expect(screen.getByLabelText('Fuente SIMIT')).toBeInTheDocument();
    expect(screen.getByLabelText('Fuente RUES')).toBeInTheDocument();
  });

  it('muestra el selector de modo SIMIT solo al marcar SIMIT', async () => {
    const user = userEvent.setup();
    render(<SourcesStep procedureTypeId="pt-1" />);
    expect(screen.queryByLabelText('Modo SIMIT')).not.toBeInTheDocument();
    await user.click(screen.getByLabelText('Fuente SIMIT'));
    expect(screen.getByLabelText('Modo SIMIT')).toBeInTheDocument();
  });

  it('guarda las fuentes seleccionadas con execution_order y simitMode', async () => {
    const user = userEvent.setup();
    render(<SourcesStep procedureTypeId="pt-7" />);

    await user.click(screen.getByLabelText('Fuente RUNT'));
    await user.click(screen.getByLabelText('Fuente SIMIT'));
    await user.click(screen.getByRole('button', { name: /guardar y continuar/i }));

    await waitFor(() => {
      expect(mocks.updateConformationProfile).toHaveBeenCalledWith('pt-7', {
        sources: [
          { sourceCode: 'RUNT', executionOrder: 1, config: {} },
          { sourceCode: 'SIMIT', executionOrder: 2, config: { simitMode: 'INTERNAL' } },
        ],
      });
    });
  });
});
