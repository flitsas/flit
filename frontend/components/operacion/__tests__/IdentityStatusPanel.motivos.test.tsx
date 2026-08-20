// HU #11666 — el historial de identidad lista, junto a las alertas, por qué NO se envió la
// validación de identidad de una parte (campo `motivosNoEnvio`, HU #11665). Misma división que la
// tarjeta del paso: bloqueo se anuncia, informativo se presenta en tono neutro.
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const mocks = vi.hoisted(() => ({
  getWizardState: vi.fn(),
  getActors: vi.fn(),
  getBiometricState: vi.fn(),
  getInstanceIdentityValidationAlerts: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({ tramitesClient: mocks }));

import { IdentityStatusPanel } from '../IdentityStatusPanel';

// Uso de ejemplo: <IdentityStatusPanel instanceId="inst-1" modalidad="traspaso" /> con el estado
// biométrico devolviendo `motivosNoEnvio`.
async function abrir() {
  const user = userEvent.setup();
  render(<IdentityStatusPanel instanceId="inst-1" modalidad="traspaso" />);
  await user.click(screen.getByRole('button', { name: /historial de identidad/i }));
  return user;
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getWizardState.mockResolvedValue({ modalidad: 'traspaso' });
  mocks.getActors.mockResolvedValue([
    {
      rol: 'comprador',
      tipoDocumento: 'NIT',
      numeroDocumento: '900123456',
      nombreCompleto: 'Transportes Andinos S.A.S.',
      email: 'contacto@example.com',
    },
  ]);
  mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({ alerts: [], total: 0 });
});

describe('IdentityStatusPanel — motivos de no envío (HU #11666)', () => {
  it('motivo bloqueante: se anuncia con role="alert" + aria-live="polite" y nombra a la parte', async () => {
    mocks.getBiometricState.mockResolvedValue({
      validations: [],
      provider: 'kyverum',
      motivosNoEnvio: [{ parte: 'comprador', codigo: 'rl_sin_documento', informativo: false }],
    });

    await abrir();

    const aviso = await screen.findByRole('alert');
    expect(aviso).toHaveAttribute('aria-live', 'polite');
    expect(aviso).toHaveTextContent('Comprador');
    expect(aviso).toHaveTextContent('Falta el documento del representante legal');
  });

  it('motivo informativo: tono neutro, sin alerta', async () => {
    mocks.getBiometricState.mockResolvedValue({
      validations: [],
      provider: 'kyverum',
      motivosNoEnvio: [{ parte: 'vendedor', codigo: 'cubierto_por_baul', informativo: true }],
    });

    await abrir();

    expect(await screen.findByText(/firma electrónica del baúl/i)).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('negativo — sin motivos el panel no agrega ningún bloque', async () => {
    mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'mock' });

    await abrir();

    expect(
      await screen.findByText(/Aún no hay aprobaciones ni rechazos registrados/i),
    ).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.queryByText(/representante legal/i)).not.toBeInTheDocument();
  });
});
