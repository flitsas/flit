import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { BiometricValidation } from '@/lib/api/types/procedure-runtime';

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  listBiometric: vi.fn(),
  simulateBiometric: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    listBiometric: mocks.listBiometric,
    simulateBiometric: mocks.simulateBiometric,
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
  score: 95,
  expiresAt: '2026-06-20T00:00:00Z',
  validadoAt: '2026-06-19T00:00:00Z',
  expired: false,
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.listBiometric.mockResolvedValue([]);
  mocks.simulateBiometric.mockResolvedValue(APROBADA);
});

describe('BiometricStep — partes por modalidad', () => {
  it('matrícula muestra solo el comprador', async () => {
    render(
      <BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" />,
    );
    expect(await screen.findByRole('group', { name: 'Biométrica Comprador' })).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Biométrica Vendedor' })).not.toBeInTheDocument();
  });

  it('traspaso muestra comprador y vendedor con un botón de simular cada uno', async () => {
    render(<BiometricStep instanceId={INSTANCE} modalidad="traspaso" />);
    expect(await screen.findByRole('group', { name: 'Biométrica Comprador' })).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'Biométrica Vendedor' })).toBeInTheDocument();
    expect(
      screen.getAllByRole('button', { name: 'Simular validación de identidad' }),
    ).toHaveLength(2);
  });
});

describe('BiometricStep — simular validación', () => {
  it('simula la validación de la parte y dispara onRefresh', async () => {
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
    await user.click(
      screen.getByRole('button', { name: 'Simular validación de identidad' }),
    );

    await waitFor(() => expect(mocks.simulateBiometric).toHaveBeenCalledTimes(1));
    const [instanceId, input] = mocks.simulateBiometric.mock.calls[0];
    expect(instanceId).toBe(INSTANCE);
    expect(input).toEqual({ parte: 'comprador' });
    await waitFor(() => expect(onRefresh).toHaveBeenCalled());
  });
});

describe('BiometricStep — resultado verificado', () => {
  it('muestra la tarjeta verde con score cuando la parte ya está aprobada', async () => {
    mocks.listBiometric.mockResolvedValue([APROBADA]);
    render(
      <BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" />,
    );
    expect(
      await screen.findByText('Identidad verificada — 95/100'),
    ).toBeInTheDocument();
    expect(screen.getByText('Ana Comprador')).toBeInTheDocument();
    // No debe ofrecer el botón de simular cuando ya hay validación aprobada.
    expect(
      screen.queryByRole('button', { name: 'Simular validación de identidad' }),
    ).not.toBeInTheDocument();
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
