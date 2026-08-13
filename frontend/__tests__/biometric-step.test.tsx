import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { BiometricValidation, ProcedureActor } from '@/lib/api/types/procedure-runtime';

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  getBiometricState: vi.fn(),
  iniciarBiometric: vi.fn(),
  simulateBiometric: vi.fn(),
  getActors: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getBiometricState: mocks.getBiometricState,
    iniciarBiometric: mocks.iniciarBiometric,
    simulateBiometric: mocks.simulateBiometric,
    getActors: mocks.getActors,
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

const ENVIADA: BiometricValidation = {
  id: 'val-5',
  partyRole: 'comprador',
  name: 'Ana Comprador',
  documentType: 'CC',
  documentNumber: '123',
  email: 'ana@example.com',
  status: 'enviado',
  intentos: 0,
  maxIntentos: 5,
  score: null,
  expiresAt: '2026-06-26T00:00:00Z',
  validatedAt: null,
  expired: false,
  provider: 'kyverum',
  captureUrl: null,
  createdAt: '2026-06-19T10:00:00Z',
};

const PENDIENTE_ENVIO: BiometricValidation = {
  id: 'val-6',
  partyRole: 'comprador',
  name: 'Ana Comprador',
  documentType: 'CC',
  documentNumber: '123',
  email: 'ana@example.com',
  status: 'pendiente_envio',
  intentos: 0,
  maxIntentos: 5,
  score: null,
  expiresAt: '2026-06-26T00:00:00Z',
  validatedAt: null,
  expired: false,
  provider: 'kyverum',
  captureUrl: null,
};

const ERROR_ENVIO: BiometricValidation = {
  id: 'val-7',
  partyRole: 'comprador',
  name: 'Ana Comprador',
  documentType: 'CC',
  documentNumber: '123',
  email: 'ana@example.com',
  status: 'error_envio',
  intentos: 0,
  maxIntentos: 5,
  score: null,
  expiresAt: '2026-06-26T00:00:00Z',
  validatedAt: null,
  expired: false,
  provider: 'kyverum',
  captureUrl: null,
};

// `en_proceso` SIN `captureUrl`: distinto de EN_PROCESO (que sí tiene enlace y cae en KyverumPendingView).
const EN_PROCESO_SIN_LINK: BiometricValidation = {
  id: 'val-8',
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
  captureUrl: null,
};

const ACTOR_COMPRADOR: ProcedureActor = {
  rol: 'comprador',
  tipoDocumento: 'CC',
  numeroDocumento: '999',
  nombreCompleto: 'Carlos Actor',
  email: 'carlos@example.com',
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'mock' });
  mocks.simulateBiometric.mockResolvedValue(APROBADA);
  mocks.iniciarBiometric.mockResolvedValue({ validation: EN_PROCESO, captureUrl: EN_PROCESO.captureUrl });
  mocks.getActors.mockResolvedValue([]);
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

  // HU21 — saliente antes que entrante, igual que el resumen de firmas y el expediente.
  it('traspaso presenta el vendedor ANTES del comprador', async () => {
    render(<BiometricStep instanceId={INSTANCE} modalidad="traspaso" />);
    await screen.findByRole('group', { name: 'Biométrica Vendedor' });
    const grupos = screen
      .getAllByRole('group')
      .map((g) => g.getAttribute('aria-label'))
      .filter((label): label is string => label?.startsWith('Biométrica ') ?? false);
    expect(grupos).toEqual(['Biométrica Vendedor', 'Biométrica Comprador']);
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
    // AC3 — la confirmación ("Se enviará el enlace a…") se interpone antes de disparar la validación.
    await user.click(await screen.findByRole('button', { name: 'Confirmar y enviar' }));

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
    // AC3 — la confirmación ("Se enviará el enlace a…") se interpone antes de disparar la validación.
    await user.click(await screen.findByRole('button', { name: 'Confirmar y enviar' }));

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
  it('aprobada: el puntaje va en el badge y el nombre en el recuadro de la persona', async () => {
    mocks.getBiometricState.mockResolvedValue({ validations: [APROBADA], provider: 'mock' });
    render(
      <BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" />,
    );
    expect(
      await screen.findByText('Aprobado — 95/100'),
    ).toBeInTheDocument();
    expect(screen.getByText(/Ana Comprador/)).toBeInTheDocument();
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

    await screen.findByText('Aprobado — 95/100');
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
    // Se busca el esqueleto POR SU NOMBRE y no por el rol a secas: `role="status"` lo usan también
    // los chips de estado de cada parte, y una aserción de "no queda ningún status" convertía
    // cualquier badge nuevo en un falso fallo — de hecho ya costó que se retirara uno que el
    // diseño pide. Lo que este test comprueba es que el esqueleto desaparece, nada más.
    expect(screen.getByRole('status', { name: /Cargando validaciones de identidad/i })).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Biométrica Comprador' })).not.toBeInTheDocument();

    // Al resolver: desaparece la carga y aparece la tarjeta de la parte.
    await act(async () => {
      resolveState({ validations: [], provider: 'mock' });
    });
    expect(await screen.findByRole('group', { name: 'Biométrica Comprador' })).toBeInTheDocument();
    expect(screen.queryByRole('status', { name: /Cargando validaciones de identidad/i })).not.toBeInTheDocument();
  });
});

// Antes, estas cuatro situaciones caían al `else` final y mostraban el mismo botón de arranque que una
// parte sin ninguna validación: el gestor podía disparar una segunda validación sobre una que ya estaba
// en vuelo, y si el envío falló nadie se lo decía. Cada test comprueba que el botón primario de arranque
// ("Validar identidad" / "Simular validación de identidad") YA NO aparece y que sí aparece el estado real.
describe('BiometricStep — estados "en vuelo" que antes caían al botón de arranque', () => {
  it('enviado: informa que ya se envió y NO ofrece "Validar identidad"', async () => {
    mocks.getBiometricState.mockResolvedValue({ validations: [ENVIADA], provider: 'kyverum' });
    render(<BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    expect(await screen.findByText(/Ya se envió la validación de Ana Comprador/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Validar identidad' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Simular validación de identidad' })).not.toBeInTheDocument();
    // El reenvío es una acción secundaria y explícita, nunca el botón primario del arranque.
    expect(screen.getByRole('button', { name: 'Reenviar validación' })).toBeInTheDocument();
    // Chip de la cabecera refleja el estado real.
    expect(screen.getByText('Enviado')).toBeInTheDocument();
  });

  it('pendiente_envio: informa que está en cola y NO ofrece "Validar identidad"', async () => {
    mocks.getBiometricState.mockResolvedValue({ validations: [PENDIENTE_ENVIO], provider: 'kyverum' });
    render(<BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    expect(
      await screen.findByText(/La validación está en cola de envío de Ana Comprador/),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Validar identidad' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Simular validación de identidad' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reenviar validación' })).toBeInTheDocument();
    expect(screen.getByText('Pendiente de envío')).toBeInTheDocument();
  });

  it('en_proceso SIN captureUrl: informa que sigue en proceso y NO ofrece "Validar identidad"', async () => {
    mocks.getBiometricState.mockResolvedValue({ validations: [EN_PROCESO_SIN_LINK], provider: 'kyverum' });
    render(<BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    expect(
      await screen.findByText(/La validación está en proceso con el proveedor de Ana Comprador/),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Validar identidad' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Simular validación de identidad' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reenviar validación' })).toBeInTheDocument();
    // No debe confundirse con KyverumPendingView (sin enlace de captura ni QR).
    expect(screen.queryByLabelText('Código QR del enlace de captura')).not.toBeInTheDocument();
    expect(screen.getAllByText('En proceso').length).toBeGreaterThan(0);
  });

  it('error_envio: muestra el fallo como error (no como espera) y ofrece reintentar', async () => {
    mocks.getBiometricState.mockResolvedValue({ validations: [ERROR_ENVIO], provider: 'kyverum' });
    render(<BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    const alerta = await screen.findByRole('alert');
    expect(alerta).toHaveTextContent('El envío de la validación falló.');
    expect(screen.queryByRole('button', { name: 'Validar identidad' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Simular validación de identidad' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reintentar envío' })).toBeInTheDocument();
    expect(screen.getByText('Error de envío')).toBeInTheDocument();
  });
});

// Recuadro que identifica a la persona detrás de la validación (paridad con la referencia del
// diseño: MatriculaInicial Step4 — "TRANSPORTES ANDINOS S.A.S — Comprador" / "Rep. Legal: …").
// Antes FLIT solo rotulaba el ROL en la cabecera de la tarjeta; el nombre de la persona no se veía
// hasta que la validación ya estaba en curso o aprobada.
describe('BiometricStep — recuadro de identidad de la parte', () => {
  it('con validación ya cargada, muestra el nombre y el documento de esa validación', async () => {
    mocks.getBiometricState.mockResolvedValue({ validations: [APROBADA], provider: 'mock' });
    render(<BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    expect(await screen.findByText('Ana Comprador — Comprador')).toBeInTheDocument();
    expect(screen.getByText('CC 123')).toBeInTheDocument();
    // No debe consultar actores si ya hay validación con los datos (igual se llama para tenerlos
    // listos por si la validación cambia, pero el recuadro no depende de esa respuesta aquí).
  });

  it('sin validación todavía (Sin iniciar), toma nombre y documento del actor del trámite', async () => {
    mocks.getActors.mockResolvedValue([ACTOR_COMPRADOR]);
    render(<BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    await screen.findByRole('group', { name: 'Biométrica Comprador' });
    expect(await screen.findByText('Carlos Actor — Comprador')).toBeInTheDocument();
    expect(screen.getByText('CC 999')).toBeInTheDocument();
  });

  it('jurídica con representante legal: usa "Rep. Legal: {nombre} · {tipoDoc} {numero}"', async () => {
    const actorJuridico: ProcedureActor = {
      rol: 'comprador',
      tipoDocumento: 'NIT',
      numeroDocumento: '900123456',
      nombreCompleto: 'TRANSPORTES ANDINOS S.A.S',
      email: 'contacto@andinos.com',
      personType: 'juridical',
      representanteLegal: {
        nombreCompleto: 'Héctor Copete Andrade',
        tipoDocumento: 'CC',
        numeroDocumento: '71654328',
      },
    };
    mocks.getActors.mockResolvedValue([actorJuridico]);
    render(<BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    await screen.findByRole('group', { name: 'Biométrica Comprador' });
    expect(
      await screen.findByText('TRANSPORTES ANDINOS S.A.S — Comprador'),
    ).toBeInTheDocument();
    expect(
      screen.getByText('Rep. Legal: Héctor Copete Andrade · CC 71654328'),
    ).toBeInTheDocument();
  });

  it('si getActors falla, la tarjeta sigue funcionando y simplemente no pinta el recuadro', async () => {
    mocks.getActors.mockRejectedValue(new Error('network'));
    render(<BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" />);

    // El resto del paso funciona igual: la tarjeta y la acción de iniciar siguen disponibles.
    expect(await screen.findByRole('group', { name: 'Biométrica Comprador' })).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Simular validación de identidad' }),
    ).toBeInTheDocument();
    // Sin actor ni validación, no hay dato para el recuadro: no se pinta nada falso.
    expect(screen.queryByText(/— Comprador$/)).not.toBeInTheDocument();
  });
});
