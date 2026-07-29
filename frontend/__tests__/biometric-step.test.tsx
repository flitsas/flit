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
  partyRole: 'comprador',
  name: 'Ana Comprador',
  documentType: 'CC',
  documentNumber: '123',
  email: 'ana@example.com',
  status: 'aprobado',
  intentos: 1,
  maxIntentos: 3,
  score: 95,
  expiresAt: '2026-06-20T00:00:00Z',
  validatedAt: '2026-06-19T00:00:00Z',
  expired: false,
  provider: 'mock',
  captureUrl: null,
};

const EN_PROCESO: BiometricValidation = {
  id: 'val-2',
  partyRole: 'comprador',
  name: 'Ana Comprador',
  documentType: 'CC',
  documentNumber: '123',
  email: 'ana@example.com',
  status: 'en_proceso',
  intentos: 0,
  maxIntentos: 5,
  score: null,
  expiresAt: '2026-06-26T00:00:00Z',
  validatedAt: null,
  expired: false,
  provider: 'kyverum',
  captureUrl: 'https://verify.kyverum.com/capture.html?t=abc',
};

const RECHAZADA: BiometricValidation = {
  id: 'val-3',
  partyRole: 'comprador',
  name: 'Ana Comprador',
  documentType: 'CC',
  documentNumber: '123',
  email: 'ana@example.com',
  status: 'rechazado',
  intentos: 1,
  maxIntentos: 5,
  score: 30,
  expiresAt: '2026-06-26T00:00:00Z',
  validatedAt: null,
  expired: false,
  provider: 'kyverum',
  captureUrl: null,
  rejectionReason: 'Los datos personales no coinciden con el documento.',
};

const EXPIRADA: BiometricValidation = {
  id: 'val-4',
  partyRole: 'comprador',
  name: 'Ana Comprador',
  documentType: 'CC',
  documentNumber: '123',
  email: 'ana@example.com',
  status: 'expirado',
  intentos: 0,
  maxIntentos: 5,
  score: null,
  expiresAt: '2026-06-20T15:30:00Z',
  validatedAt: null,
  expired: true,
  provider: 'kyverum',
  captureUrl: null,
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
        status: 'aprobado',
        score: 76,
        validatedAt: '2026-06-25T00:00:00Z',
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

describe('BiometricStep — AC2 (abrir captura Kyverum)', () => {
  it('en proceso ofrece el CTA "Abrir captura Kyverum" con target=_blank y rel=noopener', async () => {
    mocks.getBiometricState.mockResolvedValue({ validations: [EN_PROCESO], provider: 'kyverum' });
    render(<BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    const cta = await screen.findByRole('link', { name: 'Abrir captura Kyverum' });
    expect(cta).toHaveAttribute('href', EN_PROCESO.captureUrl);
    expect(cta).toHaveAttribute('target', '_blank');
    expect(cta).toHaveAttribute('rel', expect.stringContaining('noopener'));
  });
});

describe('BiometricStep — AC4 (rechazo con motivo)', () => {
  it('muestra el motivo sanitizado y ofrece "Reintentar validación"', async () => {
    mocks.getBiometricState.mockResolvedValue({ validations: [RECHAZADA], provider: 'kyverum' });
    render(<BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    expect(
      await screen.findByText(/Los datos personales no coinciden con el documento\./),
    ).toBeInTheDocument();
    expect(screen.getByText(/Validación no aprobada \(30\/100\)/)).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Reintentar validación' }),
    ).toBeInTheDocument();
  });
});

describe('BiometricStep — AC5 (expiración)', () => {
  it('muestra la fecha de expiración y ofrece "Reiniciar validación"', async () => {
    mocks.getBiometricState.mockResolvedValue({ validations: [EXPIRADA], provider: 'kyverum' });
    render(<BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    expect(
      await screen.findByText('El enlace de validación expiró.'),
    ).toBeInTheDocument();
    // La fecha se muestra formateada (es-CO): basta verificar que aparece el aviso "Venció el …".
    expect(screen.getByText(/Venció el /)).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Reiniciar validación' }),
    ).toBeInTheDocument();
  });
});

describe('BiometricStep — CF-08 (Feature #11004, HU #11009): historial completo por parte', () => {
  it('con 2+ validaciones muestra TODAS en el historial, no solo la vigente/más reciente', async () => {
    // La tarjeta de acción sigue mostrando solo la más reciente (RECHAZADA es previa a EN_PROCESO,
    // que queda como la vigente en_proceso). Ambas deben ser visibles en el historial.
    mocks.getBiometricState.mockResolvedValue({
      validations: [RECHAZADA, EN_PROCESO],
      provider: 'kyverum',
    });
    render(<BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    expect(await screen.findByText(/Historial de validaciones \(2\)/)).toBeInTheDocument();
    // Cada ítem del historial trae su propio "Ver tracking" (bitácora por validationId).
    expect(screen.getAllByRole('button', { name: /ver tracking/i })).toHaveLength(2);
    // La tarjeta de acción arriba sigue mostrando solo el estado de la vigente (en_proceso).
    expect(
      await screen.findByText(/Esperando validación de Ana Comprador/),
    ).toBeInTheDocument();
  });

  it('etiqueta "Vigente" solo la última validación de la parte', async () => {
    mocks.getBiometricState.mockResolvedValue({
      validations: [RECHAZADA, EN_PROCESO],
      provider: 'kyverum',
    });
    render(<BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    await screen.findByText(/Historial de validaciones \(2\)/);
    expect(screen.getAllByText('Vigente')).toHaveLength(1);
  });

  it('con una sola validación (sin historial real) no muestra la sección', async () => {
    mocks.getBiometricState.mockResolvedValue({ validations: [APROBADA], provider: 'mock' });
    render(<BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    await screen.findByText('Identidad verificada — 95/100');
    expect(screen.queryByText(/Historial de validaciones/)).not.toBeInTheDocument();
  });
});

describe('BiometricStep — AC8 (estado de carga)', () => {
  it('muestra un placeholder accesible (role=status) hasta que llega la primera respuesta', async () => {
    let resolveState: (v: { validations: BiometricValidation[]; provider: string }) => void = () => {};
    mocks.getBiometricState.mockReturnValue(
      new Promise((resolve) => {
        resolveState = resolve;
      }),
    );

    render(<BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    // Antes de resolver: estado de carga visible, sin tarjetas todavía.
    expect(screen.getByRole('status')).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Biométrica Comprador' })).not.toBeInTheDocument();

    // Al resolver: desaparece la carga y aparece la tarjeta de la parte.
    await act(async () => {
      resolveState({ validations: [], provider: 'mock' });
    });
    expect(await screen.findByRole('group', { name: 'Biométrica Comprador' })).toBeInTheDocument();
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });
});
