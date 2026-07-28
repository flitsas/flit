import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type {
  TenantBiometricValidation,
  TenantBiometricValidationsResponse,
} from '@/lib/api/types/procedure-runtime';

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  listTenantBiometricValidations: vi.fn(),
  listStuckIdentityValidations: vi.fn(),
  requeueStuckIdentityValidation: vi.fn(),
  requeueAllStuckIdentityValidations: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    listTenantBiometricValidations: mocks.listTenantBiometricValidations,
    listStuckIdentityValidations: mocks.listStuckIdentityValidations,
    requeueStuckIdentityValidation: mocks.requeueStuckIdentityValidation,
    requeueAllStuckIdentityValidations: mocks.requeueAllStuckIdentityValidations,
  },
}));

import { Validaciones } from '@/components/atom/modules/Validaciones';

const ROW_APROBADA: TenantBiometricValidation = {
  id: 'v-1',
  instanceId: 'inst-1',
  referenceNumber: 'TRM-2026-000001',
  modalidad: 'traspaso',
  partyRole: 'comprador',
  name: 'Ana Compradora',
  documentType: 'CC',
  documentNumber: '1020304050',
  status: 'aprobado',
  score: 95,
  provider: 'kyverum',
  expired: false,
  rejectionReason: null,
  createdAt: '2026-06-20T15:30:00Z',
  validatedAt: '2026-06-20T15:40:00Z',
  validUntil: '2026-07-20T00:00:00-05:00',
  daysRemaining: 20,
  // Aprobada: no hay enlace vigente que reenviar (el backend lo devuelve null en estados terminales).
  captureUrl: null,
  linkExpiresAt: '2026-06-21T15:30:00Z',
  email: 'ana.compradora@correo.co', // CF-05 (HU #11006)
};

const ROW_RECHAZADA: TenantBiometricValidation = {
  id: 'v-2',
  instanceId: 'inst-2',
  referenceNumber: 'TRM-2026-000002',
  modalidad: 'matricula_inicial',
  partyRole: null,
  name: 'Luis Vendedor',
  documentType: 'CC',
  documentNumber: '7788',
  status: 'rechazado',
  score: 30,
  provider: 'kyverum',
  expired: false,
  rejectionReason: 'La verificación del documento no fue exitosa.',
  createdAt: '2026-06-21T10:00:00Z',
  validatedAt: null,
  validUntil: null,
  daysRemaining: null,
  captureUrl: null,
  linkExpiresAt: null,
  email: null, // CF-05 (HU #11006) — BE aún no lo envía para esta fila (fixture de borde)
};

/** CF-05 (HU #10886, AC2) — validación EN CURSO: es la única que trae enlace vigente. */
const ROW_EN_PROCESO: TenantBiometricValidation = {
  id: 'v-3',
  instanceId: 'inst-3',
  referenceNumber: 'TRM-2026-000003',
  modalidad: 'traspaso',
  partyRole: 'vendedor',
  name: 'Carlos Vendedor',
  documentType: 'CC',
  documentNumber: '5566',
  status: 'en_proceso',
  score: null,
  provider: 'kyverum',
  expired: false,
  rejectionReason: null,
  createdAt: '2026-06-22T09:00:00Z',
  validatedAt: null,
  validUntil: null,
  daysRemaining: null,
  captureUrl: 'https://capture.kyverum.co/kyv_123',
  linkExpiresAt: '2026-06-23T09:00:00Z',
  email: 'carlos.vendedor@correo.co', // CF-05 (HU #11006)
};

const FULL: TenantBiometricValidationsResponse = {
  validations: [ROW_APROBADA, ROW_RECHAZADA],
  stats: { total: 8, aprobadas: 3, enProceso: 3, rechazadas: 1, expiradas: 1 },
  page: 1,
  pageSize: 20,
  total: 2,
};

const EMPTY: TenantBiometricValidationsResponse = {
  validations: [],
  stats: { total: 0, aprobadas: 0, enProceso: 0, rechazadas: 0, expiradas: 0 },
  page: 1,
  pageSize: 20,
  total: 0,
};

const NO_STUCK = { stuck: [], total: 0, maxDeliveryAttempts: 5 };

beforeEach(() => {
  vi.clearAllMocks();
  // Por defecto no hay eventos atascados (el banner no aparece); cada test puede sobreescribirlo.
  mocks.listStuckIdentityValidations.mockResolvedValue(NO_STUCK);
  mocks.requeueStuckIdentityValidation.mockResolvedValue({ requeued: true });
  mocks.requeueAllStuckIdentityValidations.mockResolvedValue({ requeued: 2 });
});

describe('Validaciones — AC8 estados de UI', () => {
  it('Cargando: muestra el placeholder accesible antes de la primera respuesta', async () => {
    let resolveFn: (v: TenantBiometricValidationsResponse) => void = () => {};
    const pending = new Promise<TenantBiometricValidationsResponse>((r) => {
      resolveFn = r;
    });
    mocks.listTenantBiometricValidations.mockReturnValue(pending);

    render(<Validaciones />);

    // role="status" con el texto sr-only de carga.
    const status = screen.getByRole('status');
    expect(status).toHaveTextContent(/cargando validaciones de identidad/i);

    await act(async () => {
      resolveFn(EMPTY);
      await pending;
    });
  });

  it('Vacío: muestra mensaje explícito cuando no hay validaciones', async () => {
    mocks.listTenantBiometricValidations.mockResolvedValue(EMPTY);

    render(<Validaciones />);

    expect(await screen.findByText(/aún no hay validaciones de identidad/i)).toBeInTheDocument();
  });

  it('Error: muestra role="alert" con reintento cuando falla la carga', async () => {
    mocks.listTenantBiometricValidations.mockRejectedValue(new Error('500 Internal Server Error'));

    render(<Validaciones />);

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(/no se pudieron cargar las validaciones/i);
    expect(within(alert).getByRole('button', { name: /reintentar/i })).toBeInTheDocument();
  });

  it('Lleno: pinta KPIs reales y una fila por validación', async () => {
    mocks.listTenantBiometricValidations.mockResolvedValue(FULL);

    render(<Validaciones />);

    // KPIs reales (no mock).
    expect(await screen.findByText('TRM-2026-000001')).toBeInTheDocument();
    expect(screen.getByText('TRM-2026-000002')).toBeInTheDocument();

    // Una de las tarjetas KPI muestra el total real (8).
    expect(screen.getByText('Total validaciones')).toBeInTheDocument();
    expect(screen.getByText('8')).toBeInTheDocument();

    // Badges de estado (dentro de la tabla; el toolbar también tiene chips con esos textos).
    const list = screen.getByRole('list', { name: /validaciones de identidad/i });
    expect(within(list).getByText('Aprobado')).toBeInTheDocument();
    expect(within(list).getByText('Rechazado')).toBeInTheDocument();
  });
});

describe('Validaciones — datos y accesibilidad', () => {
  beforeEach(() => {
    mocks.listTenantBiometricValidations.mockResolvedValue(FULL);
  });

  it('cada fila enlaza al trámite de origen con aria-label descriptivo', async () => {
    render(<Validaciones />);

    const link = await screen.findByRole('link', { name: /validación de ana compradora/i });
    expect(link).toHaveAttribute('href', '/tramites/inst-1');
    expect(link.getAttribute('aria-label')).toMatch(/trámite trm-2026-000001/i);
  });

  it('CF-04 (HU #11006): muestra el documento completo en la tabla, sin enmascarar', async () => {
    render(<Validaciones />);

    expect(await screen.findByText('CC 1020304050')).toBeInTheDocument();
    expect(screen.queryByText(/CC ••••4050/)).not.toBeInTheDocument();
  });

  it('CF-05 (HU #11006): muestra la columna Correo con el valor del backend y "—" si aún no llega', async () => {
    render(<Validaciones />);

    await screen.findByText('TRM-2026-000001');
    expect(screen.getByText('ana.compradora@correo.co')).toBeInTheDocument();

    // ROW_RECHAZADA no trae email todavía (BE en curso, HU #11005) — se muestra "—" sin romper la fila.
    const rechazadaLink = screen.getByRole('link', { name: /validación de luis vendedor/i });
    expect(rechazadaLink.getAttribute('aria-label')).toMatch(/correo —/i);
  });

  it('AC5 (HU #11006, CF-03, regresión): Validaciones sigue listando ambos tipos, sin enviar standalone', async () => {
    render(<Validaciones />);

    await waitFor(() => {
      expect(mocks.listTenantBiometricValidations).toHaveBeenCalled();
    });
    const lastArg = mocks.listTenantBiometricValidations.mock.calls.at(-1)?.[0];
    expect(lastArg).not.toHaveProperty('standalone');
  });

  it('muestra el motivo de rechazo sanitizado en la fila rechazada', async () => {
    render(<Validaciones />);

    expect(
      await screen.findByText('La verificación del documento no fue exitosa.'),
    ).toBeInTheDocument();
  });

  it('muestra aprobación, expiración y días restantes de vigencia en la fila aprobada', async () => {
    render(<Validaciones />);

    // La fila aprobada expone la fecha de fin de vigencia y los días restantes (badge "20 días").
    const link = await screen.findByRole('link', { name: /validación de ana compradora/i });
    // Columnas desacopladas: la grilla expone cabeceras separadas Registro / Aprobación / Vigencia.
    expect(screen.getByText('Aprobación')).toBeInTheDocument();
    // "Vigencia" aparece como cabecera de columna Y como label del filtro → debe haber ≥1.
    expect(screen.getAllByText('Vigencia').length).toBeGreaterThan(0);
    // La fila aprobada muestra el badge de días restantes (en la columna Vigencia).
    expect(within(link).getByText('20 días')).toBeInTheDocument();
    // El aria-label resume la vigencia para lectores de pantalla.
    expect(link.getAttribute('aria-label')).toMatch(/vigente hasta/i);
    expect(link.getAttribute('aria-label')).toMatch(/vigencia: 20 días restantes/i);
  });

  it('la fila sin aprobación no muestra días de vigencia (—)', async () => {
    render(<Validaciones />);

    const link = await screen.findByRole('link', { name: /validación de luis vendedor/i });
    expect(within(link).queryByText(/día/)).not.toBeInTheDocument();
    expect(link.getAttribute('aria-label')).not.toMatch(/vigente hasta/i);
  });

  it('el botón Actualizar tiene nombre accesible', async () => {
    render(<Validaciones />);

    await screen.findByText('TRM-2026-000001');
    expect(
      screen.getByRole('button', { name: /actualizar validaciones de identidad/i }),
    ).toBeInTheDocument();
  });
});

describe('Validaciones — filtros (HU #10348)', () => {
  it('filtra por estado: al elegir un valor re-consulta el backend con ese estado', async () => {
    const user = userEvent.setup();
    mocks.listTenantBiometricValidations.mockResolvedValue(FULL);

    render(<Validaciones />);
    await screen.findByText('TRM-2026-000001'); // carga inicial

    await user.selectOptions(screen.getByLabelText('Estado'), 'aprobado');

    await waitFor(() =>
      expect(mocks.listTenantBiometricValidations).toHaveBeenCalledWith(
        expect.objectContaining({ status: 'aprobado' }),
      ),
    );
  });

  it('filtra por vigencia: al elegir "Por vencer" re-consulta con vigenciaEstado', async () => {
    const user = userEvent.setup();
    mocks.listTenantBiometricValidations.mockResolvedValue(FULL);

    render(<Validaciones />);
    await screen.findByText('TRM-2026-000001');

    await user.selectOptions(screen.getByLabelText('Vigencia'), 'por_vencer');

    await waitFor(() =>
      expect(mocks.listTenantBiometricValidations).toHaveBeenCalledWith(
        expect.objectContaining({ vigenciaEstado: 'por_vencer' }),
      ),
    );
  });

  it('filtra por rango de expiración: "Vence desde/hasta" viajan como expiraDesde/expiraHasta', async () => {
    const user = userEvent.setup();
    mocks.listTenantBiometricValidations.mockResolvedValue(FULL);

    render(<Validaciones />);
    await screen.findByText('TRM-2026-000001');

    // Los date-pickers de vencimiento viven en el panel avanzado.
    await user.click(screen.getByRole('button', { name: /más filtros/i }));
    await user.type(screen.getByLabelText('Vence desde'), '2026-07-01');
    await user.type(screen.getByLabelText('Vence hasta'), '2026-07-31');

    await waitFor(() =>
      expect(mocks.listTenantBiometricValidations).toHaveBeenCalledWith(
        expect.objectContaining({
          expiraDesde: '2026-07-01T00:00:00',
          expiraHasta: '2026-07-31T23:59:59',
        }),
      ),
    );
  });

  it('filtra por "Vence en ≤ N días": el número viaja como venceEnDias', async () => {
    const user = userEvent.setup();
    mocks.listTenantBiometricValidations.mockResolvedValue(FULL);

    render(<Validaciones />);
    await screen.findByText('TRM-2026-000001');

    await user.click(screen.getByRole('button', { name: /más filtros/i }));
    await user.type(screen.getByLabelText('Vence en ≤ N días'), '3');

    await waitFor(() =>
      expect(mocks.listTenantBiometricValidations).toHaveBeenCalledWith(
        expect.objectContaining({ venceEnDias: 3 }),
      ),
    );
  });

  it('combina filtros de texto (referencia + nombre) y los envía al backend', async () => {
    const user = userEvent.setup();
    mocks.listTenantBiometricValidations.mockResolvedValue(FULL);

    render(<Validaciones />);
    await screen.findByLabelText(/filtrar por número de trámite/i);

    await user.type(screen.getByLabelText(/filtrar por número de trámite/i), 'TRM-2026');
    await user.type(screen.getByLabelText('Persona'), 'Ana');

    // Debounce ~300ms; la última consulta combina ambos filtros (AND en el backend).
    await waitFor(() =>
      expect(mocks.listTenantBiometricValidations).toHaveBeenCalledWith(
        expect.objectContaining({ referenceNumber: 'TRM-2026', name: 'Ana' }),
      ),
    );
  });

  it('sin resultados: con filtros activos y respuesta vacía muestra "Sin resultados"', async () => {
    const user = userEvent.setup();
    mocks.listTenantBiometricValidations
      .mockResolvedValueOnce(FULL) // carga inicial con datos
      .mockResolvedValue(EMPTY); // tras filtrar, sin coincidencias

    render(<Validaciones />);
    await screen.findByText('TRM-2026-000001');

    await user.selectOptions(screen.getByLabelText('Estado'), 'expirado');

    expect(await screen.findByText(/sin resultados\./i)).toBeInTheDocument();
    // NO debe mostrar el vacío inicial.
    expect(
      screen.queryByText(/aún no hay validaciones de identidad/i),
    ).not.toBeInTheDocument();
  });

  it('limpiar filtros: restablece los controles y recarga sin query params', async () => {
    const user = userEvent.setup();
    mocks.listTenantBiometricValidations.mockResolvedValue(FULL);

    render(<Validaciones />);
    await screen.findByText('TRM-2026-000001');

    const estadoSelect = screen.getByLabelText('Estado');
    await user.selectOptions(estadoSelect, 'aprobado');
    await waitFor(() => expect(estadoSelect).toHaveValue('aprobado'));

    mocks.listTenantBiometricValidations.mockClear();
    await user.click(screen.getByRole('button', { name: /limpiar filtros/i }));

    await waitFor(() => {
      expect(mocks.listTenantBiometricValidations).toHaveBeenCalled();
      const lastArg = mocks.listTenantBiometricValidations.mock.calls.at(-1)?.[0];
      expect(lastArg?.status).toBeUndefined();
    });
    // El control vuelve a "Todos" (valor vacío).
    expect(screen.getByLabelText('Estado')).toHaveValue('');
  });

  it('auto-refresca la grilla en vivo por intervalo (suscripción), sin pulsar Actualizar', async () => {
    vi.useFakeTimers();
    try {
      mocks.listTenantBiometricValidations.mockResolvedValue(FULL);
      render(<Validaciones />);

      // Resuelve la carga inicial (1 consulta).
      await act(async () => {
        await vi.advanceTimersByTimeAsync(0);
      });
      expect(mocks.listTenantBiometricValidations).toHaveBeenCalledTimes(1);

      // Al cumplirse el intervalo de auto-refresco, vuelve a consultar el backend sin interacción.
      await act(async () => {
        await vi.advanceTimersByTimeAsync(15_000);
      });
      expect(mocks.listTenantBiometricValidations).toHaveBeenCalledTimes(2);

      // La consulta automática se hace en segundo plano (último parámetro de filtros, sin query).
      const lastArg = mocks.listTenantBiometricValidations.mock.calls.at(-1)?.[0];
      expect(lastArg?.status).toBeUndefined();
    } finally {
      vi.useRealTimers();
    }
  });
});

describe('Validaciones — eventos atascados (dead-letter, HU #10349)', () => {
  const STUCK = {
    stuck: [
      {
        id: 'evt-1',
        validationId: 'val-12345678',
        eventType: 'identity_validation.completed',
        attempts: 5,
        occurredAt: '2026-06-20T10:00:00Z',
        createdAt: '2026-06-20T10:00:00Z',
        name: 'Maria Compradora',
        documentType: 'CC',
        documentNumber: '1020304050',
      },
    ],
    total: 1,
    maxDeliveryAttempts: 5,
  };

  it('muestra el banner con el nombre y el documento enmascarado de la persona', async () => {
    mocks.listTenantBiometricValidations.mockResolvedValue(FULL);
    mocks.listStuckIdentityValidations.mockResolvedValue(STUCK);

    render(<Validaciones />);

    expect(await screen.findByText(/de identidad atascada/i)).toBeInTheDocument();
    // Dentro del banner: muestra el nombre (no el validation_id) y el documento enmascarado a los últimos 4.
    const banner = screen.getByRole('region', { name: /validaciones de identidad atascadas/i });
    expect(within(banner).getByText(/Maria Compradora/)).toBeInTheDocument();
    expect(within(banner).getByText(/CC ••••4050/)).toBeInTheDocument();
    expect(within(banner).queryByText(/val-12345678/)).not.toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /reintentar la validación de maria compradora/i }),
    ).toBeInTheDocument();
  });

  it('etiqueta cada fila según la cola atascada (envío al proveedor vs. firma·FUR)', async () => {
    mocks.listTenantBiometricValidations.mockResolvedValue(FULL);
    mocks.listStuckIdentityValidations.mockResolvedValue({
      stuck: [
        { ...STUCK.stuck[0], id: 'evt-envio', kind: 'envio', name: 'Pedro Envio' },
        {
          ...STUCK.stuck[0],
          id: 'evt-cad',
          validationId: 'val-bbbbbbbb',
          kind: 'encadenamiento',
          name: 'Ana Cadena',
        },
      ],
      total: 2,
      maxDeliveryAttempts: 5,
    });

    render(<Validaciones />);

    const banner = await screen.findByRole('region', {
      name: /validaciones de identidad atascadas/i,
    });
    expect(within(banner).getByText('Envío a proveedor')).toBeInTheDocument();
    expect(within(banner).getByText('Firma · FUR')).toBeInTheDocument();
  });

  it('Reintentar reencola el evento en el backend y el banner desaparece al refrescar', async () => {
    const user = userEvent.setup();
    mocks.listTenantBiometricValidations.mockResolvedValue(FULL);
    mocks.listStuckIdentityValidations
      .mockResolvedValueOnce(STUCK) // carga inicial: hay un atascado
      .mockResolvedValue(NO_STUCK); // tras reencolar: ya no

    render(<Validaciones />);
    const btn = await screen.findByRole('button', {
      name: /reintentar la validación de maria compradora/i,
    });

    await user.click(btn);

    await waitFor(() =>
      expect(mocks.requeueStuckIdentityValidation).toHaveBeenCalledWith('evt-1'),
    );
    await waitFor(() =>
      expect(screen.queryByText(/de identidad atascada/i)).not.toBeInTheDocument(),
    );
  });

  it('no muestra el banner cuando no hay atascados', async () => {
    mocks.listTenantBiometricValidations.mockResolvedValue(FULL);
    // listStuckIdentityValidations usa el default NO_STUCK del beforeEach.

    render(<Validaciones />);

    await screen.findByText('TRM-2026-000001');
    expect(screen.queryByText(/de identidad atascada/i)).not.toBeInTheDocument();
  });

  it('Reintentar todos reencola en lote y el banner desaparece', async () => {
    const user = userEvent.setup();
    const STUCK_MANY = {
      stuck: [
        { ...STUCK.stuck[0], id: 'evt-1', validationId: 'val-aaaaaaaa' },
        {
          id: 'evt-2',
          validationId: 'val-bbbbbbbb',
          eventType: 'identity_validation.completed',
          attempts: 5,
          occurredAt: '2026-06-20T11:00:00Z',
          createdAt: '2026-06-20T11:00:00Z',
          name: 'Luis Vendedor',
          documentType: 'CC',
          documentNumber: '7788',
        },
      ],
      total: 2,
      maxDeliveryAttempts: 5,
    };
    mocks.listTenantBiometricValidations.mockResolvedValue(FULL);
    mocks.listStuckIdentityValidations
      .mockResolvedValueOnce(STUCK_MANY) // hay 2 atascados
      .mockResolvedValue(NO_STUCK); // tras reencolar todos: ninguno

    render(<Validaciones />);
    const btn = await screen.findByRole('button', {
      name: /reintentar todas las validaciones atascadas/i,
    });

    await user.click(btn);

    await waitFor(() => expect(mocks.requeueAllStuckIdentityValidations).toHaveBeenCalled());
    await waitFor(() =>
      expect(screen.queryByText(/de identidad atascada/i)).not.toBeInTheDocument(),
    );
  });
});

describe('Validaciones — paginación', () => {
  const PAGED: TenantBiometricValidationsResponse = {
    ...FULL,
    total: 45, // 45 / 20 = 3 páginas
  };

  it('navega a la página siguiente y consulta el backend con esa página', async () => {
    const user = userEvent.setup();
    mocks.listTenantBiometricValidations.mockResolvedValue(PAGED);

    render(<Validaciones />);
    await screen.findByText('TRM-2026-000001');

    expect(screen.getByText(/página 1 de 3/i)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /página siguiente/i }));

    await waitFor(() =>
      expect(mocks.listTenantBiometricValidations).toHaveBeenCalledWith(
        expect.objectContaining({ page: 2, pageSize: 20 }),
      ),
    );
  });

  it('cambiar filas por página vuelve a la página 1 con el nuevo tamaño', async () => {
    const user = userEvent.setup();
    mocks.listTenantBiometricValidations.mockResolvedValue(PAGED);

    render(<Validaciones />);
    await screen.findByText('TRM-2026-000001');

    await user.selectOptions(screen.getByLabelText('Filas por página'), '50');

    await waitFor(() =>
      expect(mocks.listTenantBiometricValidations).toHaveBeenCalledWith(
        expect.objectContaining({ page: 1, pageSize: 50 }),
      ),
    );
  });

  // CF-05 (HU #10886, AC2) — el módulo de identidad muestra el enlace vigente para reenviarlo por
  // otros medios, con su estado y su expiración.
  describe('enlace de validación vigente', () => {
    it('ofrece "Copiar enlace" con la expiración en las validaciones en curso', async () => {
      mocks.listTenantBiometricValidations.mockResolvedValue({
        ...FULL,
        validations: [ROW_EN_PROCESO],
        total: 1,
      });

      render(<Validaciones />);
      await screen.findByText('TRM-2026-000003');

      const boton = screen.getByRole('button', {
        name: /Copiar enlace de validación de Carlos Vendedor/i,
      });
      expect(boton).toHaveAttribute('title', 'https://capture.kyverum.co/kyv_123');
      expect(screen.getByText(/^Vence /)).toBeInTheDocument();
      // El estado sigue mostrándose (la fila y el KPI comparten la etiqueta).
      expect(screen.getAllByText('En proceso').length).toBeGreaterThan(0);
    });

    it('copia el enlace al portapapeles y confirma', async () => {
      const user = userEvent.setup();
      // Después de `setup()`: userEvent instala su propio stub de portapapeles y pisaría este.
      // jsdom no lo implementa y `navigator.clipboard` es de solo lectura → defineProperty.
      const writeText = vi.fn().mockResolvedValue(undefined);
      Object.defineProperty(navigator, 'clipboard', {
        value: { writeText },
        configurable: true,
      });
      mocks.listTenantBiometricValidations.mockResolvedValue({
        ...FULL,
        validations: [ROW_EN_PROCESO],
        total: 1,
      });

      render(<Validaciones />);
      await screen.findByText('TRM-2026-000003');

      await user.click(screen.getByRole('button', { name: /Copiar enlace de validación/i }));

      expect(writeText).toHaveBeenCalledWith('https://capture.kyverum.co/kyv_123');
      expect(await screen.findByText('Copiado')).toBeInTheDocument();
    });

    it('no ofrece enlace cuando la validación ya está en estado terminal', async () => {
      mocks.listTenantBiometricValidations.mockResolvedValue({
        ...FULL,
        validations: [ROW_APROBADA],
        total: 1,
      });

      render(<Validaciones />);
      await screen.findByText('TRM-2026-000001');

      expect(screen.queryByRole('button', { name: /Copiar enlace/i })).not.toBeInTheDocument();
    });
  });
});
