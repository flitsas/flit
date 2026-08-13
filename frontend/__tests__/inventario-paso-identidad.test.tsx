import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { BiometricValidation } from '@/lib/api/types/procedure-runtime';

/**
 * INVENTARIO DEL PASO DE IDENTIDAD (`BiometricStep`).
 *
 * Red de seguridad del rediseño visual: fija QUÉ tiene que seguir mostrando y ofreciendo la
 * validación de identidad de cada parte, no cómo se ve. El paso no captura formularios: lo que
 * enseña es ESTADO —quién está validado, quién no, por qué no y qué se puede hacer al respecto— y
 * ese estado es justo lo que un rediseño de tarjetas puede simplificar de más. Si desaparece el
 * enlace de captura, el motivo de un rechazo o el aviso de que la identidad quedó cubierta por la
 * firma del baúl, el gestor deja de saber por qué el trámite no avanza.
 *
 * Se consulta por rol y por rótulo accesible a propósito: son los dos contratos que sobreviven a un
 * cambio de maquetación. Cada parte se busca por su `group` («Biométrica Comprador»), nunca por su
 * posición ni por la clase de su tarjeta.
 *
 * Qué partes se piden depende de la modalidad, así que todo lo que dependa de eso se prueba
 * comparando las dos: traspaso valida al vendedor y al comprador; matrícula inicial, solo al
 * comprador.
 */

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  getBiometricState: vi.fn(),
  iniciarBiometric: vi.fn(),
  simulateBiometric: vi.fn(),
  reconcileBiometric: vi.fn(),
  getBiometricAuditByValidation: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getBiometricState: mocks.getBiometricState,
    iniciarBiometric: mocks.iniciarBiometric,
    simulateBiometric: mocks.simulateBiometric,
    reconcileBiometric: mocks.reconcileBiometric,
    getBiometricAuditByValidation: mocks.getBiometricAuditByValidation,
  },
  // Detector por forma del 409 de envío duplicado; el paso lo importa junto al cliente.
  getIdentitySendConflict: () => null,
}));

import { BiometricStep } from '@/components/operacion/BiometricStep';

const INSTANCE = 'inst-1';

const BASE = {
  documentType: 'CC',
  documentNumber: '123',
  email: 'ana@example.com',
  intentos: 0,
  maxIntentos: 5,
  expired: false,
  captureUrl: null,
} as const;

const APROBADA: BiometricValidation = {
  ...BASE,
  id: 'val-1',
  partyRole: 'comprador',
  name: 'Ana Compradora',
  status: 'aprobado',
  score: 95,
  expiresAt: '2026-09-20T00:00:00Z',
  validatedAt: '2026-08-12T00:00:00Z',
  provider: 'mock',
};

const EN_PROCESO: BiometricValidation = {
  ...BASE,
  id: 'val-2',
  partyRole: 'comprador',
  name: 'Ana Compradora',
  status: 'en_proceso',
  score: null,
  expiresAt: '2026-08-20T00:00:00Z',
  validatedAt: null,
  provider: 'kyverum',
  captureUrl: 'https://verify.kyverum.com/capture.html?t=abc',
};

const RECHAZADA: BiometricValidation = {
  ...BASE,
  id: 'val-3',
  partyRole: 'comprador',
  name: 'Ana Compradora',
  status: 'rechazado',
  score: 30,
  expiresAt: '2026-08-20T00:00:00Z',
  validatedAt: null,
  provider: 'kyverum',
  rejectionReason: 'Los datos personales no coinciden con el documento.',
};

const EXPIRADA: BiometricValidation = {
  ...BASE,
  id: 'val-4',
  partyRole: 'comprador',
  name: 'Ana Compradora',
  status: 'expirado',
  score: null,
  expiresAt: '2026-08-01T15:30:00Z',
  validatedAt: null,
  expired: true,
  provider: 'kyverum',
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'mock', firmaBaulPartes: [] });
  mocks.simulateBiometric.mockResolvedValue(APROBADA);
  mocks.iniciarBiometric.mockResolvedValue({ validation: EN_PROCESO, captureUrl: EN_PROCESO.captureUrl });
  mocks.reconcileBiometric.mockResolvedValue({ updated: false });
  mocks.getBiometricAuditByValidation.mockResolvedValue({ events: [], referencedFromOtherProcedure: false });
});

/**
 * Monta el paso tal como lo usa el asistente: panel propio con título y subtítulo dentro.
 * `hideIntro` es lo que hace el wizard para no repetir la descripción del paso.
 */
async function renderPaso(
  modalidad: 'matricula_inicial' | 'traspaso',
  props: Partial<Parameters<typeof BiometricStep>[0]> = {},
) {
  const user = userEvent.setup();
  render(
    <BiometricStep
      instanceId={INSTANCE}
      modalidad={modalidad}
      hideIntro
      heading="Identidad"
      headingSubtitle="Validación de identidad de cada parte."
      {...props}
    />,
  );
  await screen.findByRole('heading', { name: 'Identidad' });
  return user;
}

describe('BiometricStep — inventario: qué partes se validan en cada modalidad', () => {
  it('matrícula inicial valida solo al comprador', async () => {
    await renderPaso('matricula_inicial');

    expect(await screen.findByRole('group', { name: 'Biométrica Comprador' })).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Biométrica Vendedor' })).toBeNull();
  });

  it('traspaso valida al vendedor y al comprador, en ese orden', async () => {
    await renderPaso('traspaso');

    expect(await screen.findByRole('group', { name: 'Biométrica Vendedor' })).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'Biométrica Comprador' })).toBeInTheDocument();

    // El saliente antes que el entrante, igual que el resumen y el expediente.
    const partes = screen
      .getAllByRole('group')
      .map((g) => g.getAttribute('aria-label'))
      .filter((l): l is string => !!l?.startsWith('Biométrica '));
    expect(partes).toEqual(['Biométrica Vendedor', 'Biométrica Comprador']);
  });

  // El resumen del trámite embebe la captura de una sola parte dentro de su tarjeta: el filtro no
  // puede inventar partes que la modalidad no pide.
  it('embebido en el resumen se puede pedir una sola parte', async () => {
    await renderPaso('traspaso', { onlyPartes: ['vendedor'] });

    expect(await screen.findByRole('group', { name: 'Biométrica Vendedor' })).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Biométrica Comprador' })).toBeNull();
  });

  it('el paso conserva su título y su subtítulo dentro del panel', async () => {
    await renderPaso('matricula_inicial');

    expect(screen.getByRole('heading', { name: 'Identidad' })).toBeInTheDocument();
    expect(screen.getByText('Validación de identidad de cada parte.')).toBeInTheDocument();
  });
});

describe('BiometricStep — inventario: estado y acción de una parte sin validar', () => {
  it.each([
    ['mock', 'Simular validación de identidad'],
    ['kyverum', 'Validar identidad'],
  ])('con proveedor %s ofrece «%s»', async (provider, boton) => {
    mocks.getBiometricState.mockResolvedValue({ validations: [], provider, firmaBaulPartes: [] });
    await renderPaso('matricula_inicial');

    const comprador = await screen.findByRole('group', { name: 'Biométrica Comprador' });
    expect(
      within(comprador).getByText('Aún no se ha iniciado la validación de identidad de esta parte.'),
    ).toBeInTheDocument();
    expect(within(comprador).getByRole('button', { name: boton })).toBeInTheDocument();
  });

  // El envío gasta un correo al cliente: antes de mandarlo hay que confirmar. Si el rediseño se
  // come la confirmación, cada clic accidental le escribe al comprador.
  it('iniciar la validación pide confirmación y solo entonces envía', async () => {
    mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'kyverum', firmaBaulPartes: [] });
    const user = await renderPaso('matricula_inicial');

    await user.click(await screen.findByRole('button', { name: 'Validar identidad' }));

    const confirmacion = screen.getByRole('alertdialog', { name: 'Confirmar envío de validación' });
    expect(confirmacion).toHaveTextContent(/Se enviará el enlace a/);
    expect(within(confirmacion).getByRole('button', { name: 'Cancelar' })).toBeInTheDocument();
    expect(mocks.iniciarBiometric).not.toHaveBeenCalled();

    await user.click(within(confirmacion).getByRole('button', { name: 'Confirmar y enviar' }));

    await waitFor(() => expect(mocks.iniciarBiometric).toHaveBeenCalledWith(INSTANCE, { parte: 'comprador' }));
  });

  it('en traspaso cada parte inicia su propia validación', async () => {
    const user = await renderPaso('traspaso');

    const vendedor = await screen.findByRole('group', { name: 'Biométrica Vendedor' });
    await user.click(
      within(vendedor).getByRole('button', { name: 'Simular validación de identidad' }),
    );
    await user.click(
      within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Confirmar y enviar' }),
    );

    await waitFor(() =>
      expect(mocks.simulateBiometric).toHaveBeenCalledWith(INSTANCE, { parte: 'vendedor' }),
    );
  });
});

describe('BiometricStep — inventario: validación en curso (enlace y QR de captura)', () => {
  it('conserva el enlace copiable, el QR, el CTA de apertura y el correo destinatario', async () => {
    mocks.getBiometricState.mockResolvedValue({
      validations: [EN_PROCESO],
      provider: 'kyverum',
      firmaBaulPartes: [],
    });
    await renderPaso('matricula_inicial');

    const comprador = await screen.findByRole('group', { name: 'Biométrica Comprador' });

    // A quién se está esperando y a qué correo se le mandó el enlace.
    expect(within(comprador).getByText(/Esperando validación de Ana Compradora/)).toBeInTheDocument();
    expect(within(comprador).getByText(/ana@example\.com/)).toBeInTheDocument();

    // Las tres formas de hacerle llegar la captura al cliente: QR, botón de copiar y el enlace literal.
    expect(within(comprador).getByLabelText('Código QR del enlace de captura')).toBeInTheDocument();
    expect(within(comprador).getByLabelText('Copiar enlace de captura')).toBeInTheDocument();
    expect(within(comprador).getByRole('link', { name: EN_PROCESO.captureUrl! })).toHaveAttribute(
      'href',
      EN_PROCESO.captureUrl,
    );
    const abrir = within(comprador).getByRole('link', { name: 'Abrir captura Kyverum' });
    expect(abrir).toHaveAttribute('href', EN_PROCESO.captureUrl);
    expect(abrir).toHaveAttribute('target', '_blank');
  });

  it('un intento fallido que no cierra la validación se explica sin marcarla rechazada', async () => {
    mocks.getBiometricState.mockResolvedValue({
      validations: [
        {
          ...EN_PROCESO,
          intentos: 1,
          maxIntentos: 3,
          ultimoIntentoMotivo: 'La foto del documento salió movida.',
        },
      ],
      provider: 'kyverum',
      firmaBaulPartes: [],
    });
    await renderPaso('matricula_inicial');

    expect(await screen.findByText(/Intento 1 de 3 no pasó\./)).toBeInTheDocument();
    expect(screen.getByText(/La foto del documento salió movida\./)).toBeInTheDocument();
    // Sigue en curso: el enlace de captura no se retira.
    expect(screen.getByLabelText('Código QR del enlace de captura')).toBeInTheDocument();
  });

  // Self-heal cuando el webhook del proveedor no llega: la consulta directa al proveedor aparece
  // solo si la validación sigue colgada, para no invitar a consultas innecesarias.
  it('tras la espera ofrece consultar el estado directamente al proveedor', async () => {
    vi.useFakeTimers();
    try {
      mocks.getBiometricState.mockResolvedValue({
        validations: [EN_PROCESO],
        provider: 'kyverum',
        firmaBaulPartes: [],
      });
      render(
        <BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" hideIntro heading="Identidad" />,
      );

      await act(async () => {
        await vi.advanceTimersByTimeAsync(0);
      });
      expect(
        screen.queryByRole('button', {
          name: 'Actualizar estado de la validación consultando al proveedor',
        }),
      ).toBeNull();

      await act(async () => {
        await vi.advanceTimersByTimeAsync(15_000);
      });
      expect(
        screen.getByRole('button', {
          name: 'Actualizar estado de la validación consultando al proveedor',
        }),
      ).toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });
});

describe('BiometricStep — inventario: resultados de la validación', () => {
  it('aprobada muestra el sello, el puntaje y a quién pertenece', async () => {
    mocks.getBiometricState.mockResolvedValue({
      validations: [APROBADA],
      provider: 'mock',
      firmaBaulPartes: [],
    });
    await renderPaso('matricula_inicial');

    const comprador = await screen.findByRole('group', { name: 'Biométrica Comprador' });
    expect(within(comprador).getByText('Identidad verificada — 95/100')).toBeInTheDocument();
    expect(within(comprador).getByText('Ana Compradora')).toBeInTheDocument();
    // Resuelta: ya no se ofrece iniciar ni simular nada.
    expect(within(comprador).queryByRole('button', { name: /validación de identidad/i })).toBeNull();
  });

  /**
   * El certificado de identidad NO se descarga en este paso: vive en el resumen del trámite, junto
   * al resto de documentos del expediente (ver `inventario-paso-resumen.test.tsx`). Se deja fijado
   * aquí para que el rediseño no lo «recupere» por error y acabe con dos descargas distintas del
   * mismo PDF.
   */
  it('la descarga del certificado no vive en este paso', async () => {
    mocks.getBiometricState.mockResolvedValue({
      validations: [APROBADA],
      provider: 'mock',
      firmaBaulPartes: [],
    });
    await renderPaso('matricula_inicial');

    await screen.findByText('Identidad verificada — 95/100');
    expect(screen.queryByRole('button', { name: /Certificado/i })).toBeNull();
  });

  it('rechazada explica el motivo y deja reintentar', async () => {
    mocks.getBiometricState.mockResolvedValue({
      validations: [RECHAZADA],
      provider: 'kyverum',
      firmaBaulPartes: [],
    });
    await renderPaso('matricula_inicial');

    const comprador = await screen.findByRole('group', { name: 'Biométrica Comprador' });
    expect(within(comprador).getByText(/Validación no aprobada \(30\/100\)/)).toBeInTheDocument();
    expect(
      within(comprador).getByText(/Los datos personales no coinciden con el documento\./),
    ).toBeInTheDocument();
    expect(within(comprador).getByRole('button', { name: 'Reintentar validación' })).toBeInTheDocument();
  });

  it('expirada dice cuándo venció el enlace y deja reiniciar', async () => {
    mocks.getBiometricState.mockResolvedValue({
      validations: [EXPIRADA],
      provider: 'kyverum',
      firmaBaulPartes: [],
    });
    await renderPaso('matricula_inicial');

    const comprador = await screen.findByRole('group', { name: 'Biométrica Comprador' });
    expect(within(comprador).getByText('El enlace de validación expiró.')).toBeInTheDocument();
    expect(within(comprador).getByText(/^Venció el /)).toBeInTheDocument();
    expect(within(comprador).getByRole('button', { name: 'Reiniciar validación' })).toBeInTheDocument();
  });

  it('con varias validaciones conserva el historial completo de la parte', async () => {
    mocks.getBiometricState.mockResolvedValue({
      validations: [RECHAZADA, EN_PROCESO],
      provider: 'kyverum',
      firmaBaulPartes: [],
    });
    await renderPaso('matricula_inicial');

    expect(await screen.findByText(/Historial de validaciones \(2\)/)).toBeInTheDocument();
    // Cada intento conserva su bitácora y solo el último se rotula como vigente.
    expect(screen.getAllByRole('button', { name: /ver tracking/i })).toHaveLength(2);
    expect(screen.getAllByText('Vigente')).toHaveLength(1);
  });
});

describe('BiometricStep — inventario: identidad cubierta por la firma del baúl', () => {
  // HU #10646 — la parte jurídica que firma con el baúl no necesita biométrica; lo que no puede es
  // quedarse sin explicación, porque el gestor la vería como «pendiente» para siempre.
  it.each([
    ['la señal del registro (prop)', { vaultCoveredPartes: ['comprador' as const] }, [] as string[]],
    ['el estado del servidor', {}, ['comprador']],
  ])('se anuncia como firma electrónica desde %s', async (_origen, props, firmaBaulPartes) => {
    mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'kyverum', firmaBaulPartes });
    await renderPaso('matricula_inicial', props);

    const comprador = await screen.findByRole('group', { name: 'Biométrica Comprador' });
    expect(within(comprador).getByText('Firma electrónica (baúl)')).toBeInTheDocument();
    expect(
      within(comprador).getByText(/No requiere validación biométrica\./),
    ).toBeInTheDocument();
    // Sin biométrica que iniciar ni historial que auditar.
    expect(within(comprador).queryByRole('button', { name: /validación de identidad/i })).toBeNull();
    expect(within(comprador).queryByText(/Historial de validaciones/)).toBeNull();
  });

  it('en traspaso cubre solo a la parte que firmó con el baúl', async () => {
    mocks.getBiometricState.mockResolvedValue({
      validations: [],
      provider: 'kyverum',
      firmaBaulPartes: ['vendedor'],
    });
    await renderPaso('traspaso');

    const vendedor = await screen.findByRole('group', { name: 'Biométrica Vendedor' });
    const comprador = screen.getByRole('group', { name: 'Biométrica Comprador' });
    expect(within(vendedor).getByText('Firma electrónica (baúl)')).toBeInTheDocument();
    expect(within(comprador).queryByText('Firma electrónica (baúl)')).toBeNull();
    expect(within(comprador).getByRole('button', { name: 'Validar identidad' })).toBeInTheDocument();
  });
});

describe('BiometricStep — inventario: refrescar el estado y avisar al asistente', () => {
  it('el botón de actualizar re-consulta al proveedor y notifica al wizard', async () => {
    const onRefresh = vi.fn();
    const user = await renderPaso('matricula_inicial', { onRefresh });

    await screen.findByRole('group', { name: 'Biométrica Comprador' });
    await user.click(screen.getByRole('button', { name: 'Actualizar estado biométrico' }));

    await waitFor(() => expect(onRefresh).toHaveBeenCalled());
    expect(mocks.getBiometricState.mock.calls.length).toBeGreaterThanOrEqual(2);
  });

  it('con todas las partes resueltas por el baúl ya no hay nada que actualizar', async () => {
    mocks.getBiometricState.mockResolvedValue({
      validations: [],
      provider: 'kyverum',
      firmaBaulPartes: ['vendedor', 'comprador'],
    });
    await renderPaso('traspaso');

    await screen.findByRole('group', { name: 'Biométrica Vendedor' });
    expect(screen.queryByRole('button', { name: 'Actualizar estado biométrico' })).toBeNull();
  });

  it('mientras carga anuncia la espera, y si falla lo dice como alerta', async () => {
    // Carga: la primera respuesta aún no llegó.
    let resolver: (v: unknown) => void = () => {};
    mocks.getBiometricState.mockReturnValue(new Promise((r) => { resolver = r; }));
    const { unmount } = render(
      <BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" hideIntro heading="Identidad" />,
    );
    expect(screen.getByText('Cargando validaciones de identidad…')).toBeInTheDocument();
    await act(async () => {
      resolver({ validations: [], provider: 'mock', firmaBaulPartes: [] });
    });
    unmount();

    // Fallo: el paso no se queda mudo.
    mocks.getBiometricState.mockRejectedValue(new Error('No se pudo consultar el proveedor.'));
    render(
      <BiometricStep instanceId={INSTANCE} modalidad="matricula_inicial" hideIntro heading="Identidad" />,
    );
    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo consultar el proveedor.');
  });
});
