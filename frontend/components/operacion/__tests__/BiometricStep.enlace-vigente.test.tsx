// HU #10886 (AC2) — módulo de identidad del trámite: para una validación vigente (Kyverum en
// proceso, con enlace de captura), la UI debe mostrar el enlace con "Copiar enlace", su ESTADO
// (badge) y su fecha de EXPIRACIÓN.
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { BiometricStep } from '../BiometricStep';
import { WizardReadOnlyProvider } from '../WizardReadOnlyContext';

const mocks = vi.hoisted(() => ({
  getBiometricState: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: { getBiometricState: mocks.getBiometricState },
}));
vi.mock('@/lib/api/client', () => ({ getToken: () => null }));
vi.mock('@/lib/auth/jwt', () => ({
  decodeJwtPayload: () => null,
  isSuperAdmin: () => false,
}));

const VALIDATION = {
  id: 'v1',
  partyRole: null,
  name: 'Juan Perez',
  documentType: 'CC',
  documentNumber: '123',
  email: 'juan@example.com',
  status: 'en_proceso',
  intentos: 0,
  maxIntentos: 3,
  score: null,
  expiresAt: '2026-08-01T15:30:00Z',
  validatedAt: null,
  expired: false,
  provider: 'kyverum',
  captureUrl: 'https://kyverum.example/capture/abc123',
};

function renderStep() {
  return render(
    <WizardReadOnlyProvider readOnly={false}>
      <BiometricStep instanceId="inst-1" modalidad="matricula_inicial" />
    </WizardReadOnlyProvider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  Object.defineProperty(navigator, 'clipboard', {
    value: { writeText: vi.fn().mockResolvedValue(undefined) },
    configurable: true,
    writable: true,
  });
});

describe('BiometricStep — enlace de validación vigente (HU #10886 AC2)', () => {
  it('muestra el enlace, el botón Copiar enlace, el estado y la expiración', async () => {
    mocks.getBiometricState.mockResolvedValue({ validations: [VALIDATION], provider: 'kyverum' });
    renderStep();

    const card = await screen.findByRole('group', { name: /Biométrica Comprador/i });
    // Estado (badge reutilizado del vocabulario de Validaciones de Identidad).
    expect(within(card).getByRole('status', { name: /Estado de la validación: En proceso/i })).toBeInTheDocument();
    // Enlace + botón de copiar.
    expect(within(card).getByRole('button', { name: 'Copiar enlace de captura' })).toBeInTheDocument();
    expect(within(card).getByText(VALIDATION.captureUrl)).toBeInTheDocument();
    // Expiración.
    expect(within(card).getByText(/Vigente hasta/)).toBeInTheDocument();
  });

  it('clic en Copiar enlace usa la clipboard API y da feedback accesible "Copiado"', async () => {
    mocks.getBiometricState.mockResolvedValue({ validations: [VALIDATION], provider: 'kyverum' });
    renderStep();

    const card = await screen.findByRole('group', { name: /Biométrica Comprador/i });
    const copyButton = within(card).getByRole('button', { name: 'Copiar enlace de captura' });
    fireEvent.click(copyButton);

    await waitFor(() =>
      expect(navigator.clipboard.writeText).toHaveBeenCalledWith(VALIDATION.captureUrl),
    );
    expect(await within(card).findByText('Copiado')).toBeInTheDocument();
    expect(within(card).getByText('Enlace copiado al portapapeles.')).toBeInTheDocument();
  });
});
