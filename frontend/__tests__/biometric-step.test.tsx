import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { BiometricValidation } from '@/lib/api/types/procedure-runtime';

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  getBiometricState: vi.fn(),
  iniciarBiometric: vi.fn(),
  simulateBiometric: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getBiometricState: mocks.getBiometricState,
    iniciarBiometric: mocks.iniciarBiometric,
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
  provider: 'mock',
  captureUrl: null,
};

const EN_PROCESO: BiometricValidation = {
  id: 'val-2',
  parte: 'comprador',
  nombre: 'Ana Comprador',
  tipoDoc: 'CC',
  documento: '123',
  email: 'ana@example.com',
  estado: 'en_proceso',
  intentos: 0,
  maxIntentos: 5,
  score: null,
  expiresAt: '2026-06-26T00:00:00Z',
  validadoAt: null,
  expired: false,
  provider: 'kyverum',
  captureUrl: 'https://verify.kyverum.com/capture.html?t=abc',
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'mock' });
  mocks.simulateBiometric.mockResolvedValue(APROBADA);
  mocks.iniciarBiometric.mockResolvedValue({ validation: EN_PROCESO, captureUrl: EN_PROCESO.captureUrl });
});

describe('BiometricStep — partes por modalidad', () => {
  it('matrícula muestra solo el comprador', async () => {
    render(
      <BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" />,
    );
    expect(await screen.findByRole('group', { name: 'Biométrica Comprador' })).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Biométrica Vendedor' })).not.toBeInTheDocument();
  });

  it('traspaso muestra comprador y vendedor con un botón de simular cada uno (mock)', async () => {
    render(<BiometricStep instanceId={INSTANCE} modalidad="traspaso" />);
    expect(await screen.findByRole('group', { name: 'Biométrica Comprador' })).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'Biométrica Vendedor' })).toBeInTheDocument();
    expect(
      screen.getAllByRole('button', { name: 'Simular validación de identidad' }),
    ).toHaveLength(2);
  });
});

describe('BiometricStep — mock (simular)', () => {
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

describe('BiometricStep — kyverum (validación real)', () => {
  it('el botón inicia la validación enviando solo la parte (datos del trámite)', async () => {
    mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'kyverum' });
    const user = userEvent.setup();
    render(<BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    await user.click(await screen.findByRole('button', { name: 'Validar identidad' }));

    await waitFor(() => expect(mocks.iniciarBiometric).toHaveBeenCalledTimes(1));
    const [instanceId, input] = mocks.iniciarBiometric.mock.calls[0];
    expect(instanceId).toBe(INSTANCE);
    // Solo la parte: el backend resuelve nombre/documento/email del actor del trámite.
    expect(input).toEqual({ parte: 'comprador' });
  });

  it('en proceso muestra el enlace de captura y el QR', async () => {
    mocks.getBiometricState.mockResolvedValue({ validations: [EN_PROCESO], provider: 'kyverum' });
    render(<BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    expect(
      await screen.findByText(/Esperando validación de Ana Comprador/),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('link', { name: EN_PROCESO.captureUrl! }),
    ).toHaveAttribute('href', EN_PROCESO.captureUrl);
    expect(screen.getByLabelText('Código QR del enlace de captura')).toBeInTheDocument();
  });

  // Regresión (VERONICA): el resultado de Kyverum llega async por webhook. El polling debe avisar al
  // wizard (onRefresh) al resolverse la validación para que el gate server-driven habilite "Continuar"
  // SIN requerir un clic manual en "Actualizar". Antes solo se notificaba en el refresh manual.
  it('polling: al resolverse en_proceso → aprobado dispara onRefresh sin clic manual', async () => {
    vi.useFakeTimers();
    try {
      const onRefresh = vi.fn();
      const APROBADA_KYVERUM: BiometricValidation = {
        ...EN_PROCESO,
        estado: 'aprobado',
        score: 76,
        validadoAt: '2026-06-25T00:00:00Z',
      };
      // 1ª consulta (carga inicial): en proceso → arranca el polling, sin notificar.
      // 2ª consulta (tick de 5s): aprobado → debe notificar exactamente una vez.
      mocks.getBiometricState
        .mockResolvedValueOnce({ validations: [EN_PROCESO], provider: 'kyverum' })
        .mockResolvedValue({ validations: [APROBADA_KYVERUM], provider: 'kyverum' });

      render(
        <BiometricStep
          instanceId={INSTANCE}
          modalidad="matricula_inicial"
          onRefresh={onRefresh}
        />,
      );

      // Drena la carga inicial (en_proceso) + montaje del polling: aún NO debe avisar al wizard.
      await act(async () => { await vi.advanceTimersByTimeAsync(0); });
      expect(onRefresh).not.toHaveBeenCalled();

      // Un ciclo de polling (5s): la validación resuelve a aprobado → notifica.
      await act(async () => { await vi.advanceTimersByTimeAsync(5000); });
      expect(onRefresh).toHaveBeenCalledTimes(1);

      // No re-notifica en ciclos posteriores: ya no queda nada en_proceso → el intervalo se desmonta.
      await act(async () => { await vi.advanceTimersByTimeAsync(5000); });
      expect(onRefresh).toHaveBeenCalledTimes(1);
    } finally {
      vi.useRealTimers();
    }
  });
});

describe('BiometricStep — resultado verificado', () => {
  it('muestra la tarjeta verde con score cuando la parte ya está aprobada', async () => {
    mocks.getBiometricState.mockResolvedValue({ validations: [APROBADA], provider: 'mock' });
    render(
      <BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" />,
    );
    expect(
      await screen.findByText('Identidad verificada — 95/100'),
    ).toBeInTheDocument();
    expect(screen.getByText('Ana Comprador')).toBeInTheDocument();
    // No debe ofrecer botón de iniciar/simular cuando ya hay validación aprobada.
    expect(
      screen.queryByRole('button', { name: /validación de identidad/i }),
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
    expect(mocks.getBiometricState.mock.calls.length).toBeGreaterThanOrEqual(2);
  });
});
