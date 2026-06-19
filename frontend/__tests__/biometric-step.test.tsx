import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { BiometricValidation } from '@/lib/api/types/procedure-runtime';

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  listBiometric: vi.fn(),
  iniciarBiometric: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    listBiometric: mocks.listBiometric,
    iniciarBiometric: mocks.iniciarBiometric,
  },
}));

import { BiometricStep } from '@/components/operacion/BiometricStep';

const INSTANCE = 'inst-1';

const APROBADA: BiometricValidation = {
  id: 'val-1',
  parte: 'comprador',
  nombre: 'Ana Comprador',
  tipoDoc: 'CC',
  documento: '123',
  email: 'ana@example.com',
  estado: 'aprobado',
  intentos: 1,
  maxIntentos: 3,
  score: 92,
  expiresAt: '2026-06-20T00:00:00Z',
  validadoAt: '2026-06-19T00:00:00Z',
  expired: false,
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.listBiometric.mockResolvedValue([]);
  mocks.iniciarBiometric.mockResolvedValue({
    validation: { ...APROBADA, estado: 'enviado', score: null },
    token: 'raw-token-abc',
    magicLinkPath: '/biometric/raw-token-abc',
  });
});

describe('BiometricStep — partes por modalidad', () => {
  it('matrícula muestra solo el comprador', async () => {
    render(
      <BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" />,
    );
    expect(await screen.findByRole('group', { name: 'Biométrica Comprador' })).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Biométrica Vendedor' })).not.toBeInTheDocument();
  });

  it('traspaso muestra comprador y vendedor', async () => {
    render(<BiometricStep instanceId={INSTANCE} modalidad="traspaso" />);
    expect(await screen.findByRole('group', { name: 'Biométrica Comprador' })).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'Biométrica Vendedor' })).toBeInTheDocument();
  });
});

describe('BiometricStep — iniciar y magic-link', () => {
  it('genera el enlace al iniciar y permite copiarlo', async () => {
    const user = userEvent.setup();
    render(
      <BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" />,
    );

    await screen.findByRole('group', { name: 'Biométrica Comprador' });
    await user.type(screen.getByLabelText('Nombre completo'), 'Ana Comprador');
    await user.type(screen.getByLabelText('Número de documento'), '123');
    await user.type(screen.getByLabelText('Correo electrónico'), 'ana@example.com');
    await user.click(screen.getByRole('button', { name: 'Generar enlace biométrico' }));

    await waitFor(() => expect(mocks.iniciarBiometric).toHaveBeenCalledTimes(1));
    const [instanceId, input] = mocks.iniciarBiometric.mock.calls[0];
    expect(instanceId).toBe(INSTANCE);
    expect(input).toMatchObject({
      parte: 'comprador',
      nombre: 'Ana Comprador',
      tipoDoc: 'CC',
      documento: '123',
      email: 'ana@example.com',
    });

    const link = (await screen.findByLabelText(
      'Enlace biométrico Comprador',
    )) as HTMLInputElement;
    expect(link.value).toContain('/biometric/raw-token-abc');
  });

  it('valida campos requeridos antes de llamar al cliente', async () => {
    const user = userEvent.setup();
    render(
      <BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" />,
    );
    await screen.findByRole('group', { name: 'Biométrica Comprador' });
    await user.click(screen.getByRole('button', { name: 'Generar enlace biométrico' }));
    expect(mocks.iniciarBiometric).not.toHaveBeenCalled();
    expect(await screen.findByText(/Completa nombre, documento y correo/)).toBeInTheDocument();
  });
});

describe('BiometricStep — estado y badges', () => {
  it('muestra el badge aprobado y el score de una validación existente', async () => {
    mocks.listBiometric.mockResolvedValue([APROBADA]);
    render(
      <BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" />,
    );
    expect(await screen.findByText('Aprobado')).toBeInTheDocument();
    expect(screen.getByText('92')).toBeInTheDocument();
    expect(screen.getByText('Identidad verificada')).toBeInTheDocument();
    // No debe ofrecer el formulario de iniciar cuando ya hay validación.
    expect(screen.queryByRole('button', { name: 'Generar enlace biométrico' })).not.toBeInTheDocument();
  });

  it('al actualizar re-consulta y dispara onRefresh', async () => {
    const onRefresh = vi.fn();
    const user = userEvent.setup();
    render(
      <BiometricStep
        instanceId={INSTANCE}
        modalidad="matricula_inicial"
        onRefresh={onRefresh}
      />,
    );
    await screen.findByRole('group', { name: 'Biométrica Comprador' });
    await user.click(screen.getByRole('button', { name: 'Actualizar estado biométrico' }));
    await waitFor(() => expect(onRefresh).toHaveBeenCalled());
    // 1 carga inicial + 1 al actualizar.
    expect(mocks.listBiometric.mock.calls.length).toBeGreaterThanOrEqual(2);
  });
});
