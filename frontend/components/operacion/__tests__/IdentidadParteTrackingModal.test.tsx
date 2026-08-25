import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { IdentidadParteTrackingModal } from '@/components/operacion/IdentidadParteTrackingModal';

const listBiometricExpediente = vi.fn();
const getBiometricAuditByValidation = vi.fn();

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    listBiometricExpediente: (...args: unknown[]) => listBiometricExpediente(...args),
    getBiometricAuditByValidation: (...args: unknown[]) => getBiometricAuditByValidation(...args),
  },
}));

describe('IdentidadParteTrackingModal', () => {
  beforeEach(() => {
    listBiometricExpediente.mockReset();
    getBiometricAuditByValidation.mockReset();
    getBiometricAuditByValidation.mockResolvedValue({ events: [], referencedFromOtherProcedure: false });
  });

  it('con baúl sin bitácora muestra vacío explicativo', async () => {
    listBiometricExpediente.mockResolvedValue({
      validations: [],
      firmaBaulPartes: ['comprador'],
      provider: 'mock',
    });
    render(
      <IdentidadParteTrackingModal
        open
        instanceId="inst-1"
        parte="comprador"
        rotulo="Comprador"
        onClose={() => undefined}
      />,
    );
    expect(
      await screen.findByText(/cubierta por firma electrónica \(baúl\)/i),
    ).toBeInTheDocument();
  });

  it('con validación Kyverum monta el panel de tracking', async () => {
    listBiometricExpediente.mockResolvedValue({
      validations: [
        {
          id: 'val-1',
          partyRole: 'comprador',
          provider: 'kyverum',
          status: 'aprobado',
        },
      ],
      firmaBaulPartes: [],
      provider: 'kyverum',
    });
    render(
      <IdentidadParteTrackingModal
        open
        instanceId="inst-1"
        parte="comprador"
        rotulo="Comprador"
        onClose={() => undefined}
      />,
    );
    await waitFor(() => expect(getBiometricAuditByValidation).toHaveBeenCalledWith('val-1'));
  });
});
