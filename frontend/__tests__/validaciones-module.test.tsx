import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type {
  TenantBiometricPerson,
  TenantBiometricPersonsResponse,
  TenantBiometricValidation,
} from '@/lib/api/types/procedure-runtime';

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  listTenantBiometricPersons: vi.fn(),
  listPersonBiometricValidations: vi.fn(),
  listStuckIdentityValidations: vi.fn(),
  requeueStuckIdentityValidation: vi.fn(),
  requeueAllStuckIdentityValidations: vi.fn(),
  getBiometricAuditByValidation: vi.fn(),
  createPrevalidacion: vi.fn(),
  editPrevalidacion: vi.fn(),
  resendPrevalidacion: vi.fn(),
  iniciarBiometric: vi.fn(),
  simulateBiometric: vi.fn(),
  setActiveTramitesTenant: vi.fn(),
  listCompanies: vi.fn(),
  isSuperAdmin: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    listTenantBiometricPersons: mocks.listTenantBiometricPersons,
    listPersonBiometricValidations: mocks.listPersonBiometricValidations,
    listStuckIdentityValidations: mocks.listStuckIdentityValidations,
    requeueStuckIdentityValidation: mocks.requeueStuckIdentityValidation,
    requeueAllStuckIdentityValidations: mocks.requeueAllStuckIdentityValidations,
    getBiometricAuditByValidation: mocks.getBiometricAuditByValidation,
    createPrevalidacion: mocks.createPrevalidacion,
    editPrevalidacion: mocks.editPrevalidacion,
    resendPrevalidacion: mocks.resendPrevalidacion,
    iniciarBiometric: mocks.iniciarBiometric,
    simulateBiometric: mocks.simulateBiometric,
  },
  setActiveTramitesTenant: mocks.setActiveTramitesTenant,
  TramitesApiError: class TramitesApiError extends Error {
    constructor(
      public readonly status: number,
      message: string,
      public readonly problem: Record<string, unknown> | null = null,
    ) {
      super(message);
      this.name = 'TramitesApiError';
    }
  },
  getIdentitySendConflict: (err: unknown) => {
    if (!err || typeof err !== 'object') return null;
    const { status, problem } = err as { status?: unknown; problem?: unknown };
    if (status !== 409 || !problem || typeof problem !== 'object') return null;
    const p = problem as Record<string, unknown>;
    if (typeof p.motivo !== 'string' || !p.motivo) return null;
    return {
      motivo: p.motivo,
      status: typeof p.status === 'string' ? p.status : null,
      validatedAt: typeof p.validatedAt === 'string' ? p.validatedAt : null,
      validUntil: typeof p.validUntil === 'string' ? p.validUntil : null,
      validationId: typeof p.validationId === 'string' ? p.validationId : null,
      origen: typeof p.origen === 'string' ? p.origen : null,
    };
  },
}));

// El selector de empresa solo existe para el admin FLIT: se controla el rol desde el JWT.
vi.mock('@/lib/api/superadmin-client', () => ({
  superadminClient: { listCompanies: mocks.listCompanies },
}));
vi.mock('@/lib/api/client', () => ({ getToken: () => 'token-de-prueba' }));
vi.mock('@/lib/auth/jwt', () => ({
  decodeJwtPayload: () => ({}),
  isSuperAdmin: () => mocks.isSuperAdmin(),
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

/** Enlace que todavía no ha vencido: el vencimiento se compara contra el reloj real, no contra fixtures. */
const LINK_FUTURO = new Date(Date.now() + 12 * 60 * 60 * 1000).toISOString();
const LINK_VENCIDO = new Date(Date.now() - 60 * 60 * 1000).toISOString();

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
  linkExpiresAt: LINK_FUTURO,
  email: 'carlos.vendedor@correo.co', // CF-05 (HU #11006)
};

/**
 * El módulo unificado de Identidad tiene UNA sola grilla, agrupada POR PERSONA (una fila por
 * documento, sin repetir cédula) — GET /biometric-validations/by-person. Los fixtures se escriben
 * como validaciones (que es lo que pinta la fila) y se proyectan al DTO de persona con este mapper;
 * el desglose por validación vive en el detalle de la persona, no en la grilla.
 */
function toPerson(v: TenantBiometricValidation, validationCount = 1): TenantBiometricPerson {
  return {
    documentType: v.documentType,
    documentNumber: v.documentNumber,
    name: v.name,
    status: v.status,
    validationCount,
    worstAlertKind: null,
    latestValidationId: v.id,
    instanceId: v.instanceId,
    referenceNumber: v.referenceNumber,
    modalidad: v.modalidad,
    partyRole: v.partyRole,
    email: v.email ?? '',
    provider: v.provider as TenantBiometricPerson['provider'],
    score: v.score,
    captureUrl: v.captureUrl,
    expired: v.expired,
    createdAt: v.createdAt,
    validatedAt: v.validatedAt,
    validUntil: v.validUntil,
    daysRemaining: v.daysRemaining,
    linkExpiresAt: v.linkExpiresAt,
  };
}

/** Respuesta agrupada a partir de fixtures de validación. */
function personsResponse(
  rows: TenantBiometricValidation[],
  overrides: Partial<TenantBiometricPersonsResponse> = {},
): TenantBiometricPersonsResponse {
  return {
    persons: rows.map((r) => toPerson(r)),
    stats: { total: 8, aprobadas: 3, enProceso: 3, rechazadas: 1, expiradas: 1 },
    page: 1,
    pageSize: 20,
    total: rows.length,
    ...overrides,
  };
}

const FULL: TenantBiometricPersonsResponse = personsResponse([ROW_APROBADA, ROW_RECHAZADA]);

const EMPTY: TenantBiometricPersonsResponse = personsResponse([], {
  stats: { total: 0, aprobadas: 0, enProceso: 0, rechazadas: 0, expiradas: 0 },
});

const NO_STUCK = { stuck: [], total: 0, maxDeliveryAttempts: 5 };

/** Historial vacío por persona: evita que el drawer de detalle falle cuando no se está probando. */
const EMPTY_PERSON_HISTORY = {
  documentType: 'CC',
  documentNumber: '0',
  name: null,
  validations: [],
  page: 1,
  pageSize: 50,
  total: 0,
  allTerminal: true,
};

function renderValidaciones() {
  return render(<Validaciones />);
}

beforeEach(() => {
  vi.clearAllMocks();
  // Por defecto no hay eventos atascados (el banner no aparece); cada test puede sobreescribirlo.
  mocks.listStuckIdentityValidations.mockResolvedValue(NO_STUCK);
  mocks.listTenantBiometricPersons.mockResolvedValue(EMPTY);
  mocks.listPersonBiometricValidations.mockResolvedValue(EMPTY_PERSON_HISTORY);
  mocks.requeueStuckIdentityValidation.mockResolvedValue({ requeued: true });
  mocks.requeueAllStuckIdentityValidations.mockResolvedValue({ requeued: 2 });
  // Por defecto, usuario de compañía: sin selector de empresa.
  mocks.isSuperAdmin.mockReturnValue(false);
  mocks.listCompanies.mockResolvedValue({ data: [] });
});

describe('Validaciones — AC8 estados de UI', () => {
  it('Cargando: muestra el placeholder accesible antes de la primera respuesta', async () => {
    // La carga inicial usa la vista por persona: es esa promesa la que debe quedar pendiente para
    // ver el placeholder de la grilla.
    let resolveFn: (v: TenantBiometricPersonsResponse) => void = () => {};
    const pending = new Promise<TenantBiometricPersonsResponse>((r) => {
      resolveFn = r;
    });
    mocks.listTenantBiometricPersons.mockReturnValue(pending);

    render(<Validaciones />); // sin conmutar de vista: se mide el primer render

    // role="status" con el texto sr-only de carga (la grilla; el panel de atascadas tiene el suyo).
    const status = screen.getByText(/cargando validaciones de identidad/i).closest('[role="status"]');
    expect(status).toBeTruthy();

    await act(async () => {
      resolveFn(EMPTY);
      await pending;
    });
  });

  it('Vacío: muestra mensaje explícito cuando no hay validaciones', async () => {
    mocks.listTenantBiometricPersons.mockResolvedValue(EMPTY);

    renderValidaciones();

    expect(await screen.findByText(/aún no hay validaciones de identidad/i)).toBeInTheDocument();
  });

  it('Error: muestra role="alert" con reintento cuando falla la carga', async () => {
    mocks.listTenantBiometricPersons.mockRejectedValue(new Error('500 Internal Server Error'));

    renderValidaciones();

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(/no se pudieron cargar las validaciones/i);
    expect(within(alert).getByRole('button', { name: /reintentar/i })).toBeInTheDocument();
  });

  it('Lleno: pinta KPIs reales y una fila por persona', async () => {
    mocks.listTenantBiometricPersons.mockResolvedValue(FULL);

    renderValidaciones();

    // KPIs reales (no mock).
    expect(await screen.findByText('TRM-2026-000001')).toBeInTheDocument();
    expect(screen.getByText('TRM-2026-000002')).toBeInTheDocument();

    // El contador principal cuenta PERSONAS (`total` del endpoint agrupado = 2 filas), no las 8
    // validaciones de `stats`: tiene que cuadrar con lo que se ve en la grilla.
    const totalCard = screen.getByText('Total personas').closest('div')!;
    expect(within(totalCard).getByText('2')).toBeInTheDocument();
    expect(screen.queryByText('Total validaciones')).not.toBeInTheDocument();

    // Badges de estado (dentro de la tabla; el toolbar también tiene chips con esos textos).
    const list = screen.getByRole('list', { name: /validaciones de identidad/i });
    expect(within(list).getByText('Aprobado')).toBeInTheDocument();
    expect(within(list).getByText('Rechazado')).toBeInTheDocument();
  });
});

describe('Validaciones — datos y accesibilidad', () => {
  beforeEach(() => {
    mocks.listTenantBiometricPersons.mockResolvedValue(FULL);
  });

  it('cada fila es no navegable y expone aria-label descriptivo (sin enlace al trámite)', async () => {
    renderValidaciones();

    const row = await screen.findByLabelText(/^validación de ana compradora/i);
    expect(row.closest('a')).toBeNull();
    expect(screen.queryByRole('link', { name: /validación de ana compradora/i })).toBeNull();
    expect(row.getAttribute('aria-label')).toMatch(/trámite trm-2026-000001/i);
    expect(row.getAttribute('aria-label')).not.toMatch(/abrir trámite/i);
  });

  it('CF-04 (HU #11006): muestra el documento completo en la tabla, sin enmascarar', async () => {
    renderValidaciones();

    expect(await screen.findByText('CC 1020304050')).toBeInTheDocument();
    expect(screen.queryByText(/CC ••••4050/)).not.toBeInTheDocument();
  });

  it('CF-05 (HU #11006): muestra la columna Correo con el valor del backend y "—" si aún no llega', async () => {
    renderValidaciones();

    await screen.findByText('TRM-2026-000001');
    expect(screen.getByText('ana.compradora@correo.co')).toBeInTheDocument();

    // ROW_RECHAZADA no trae email todavía (BE en curso, HU #11005) — se muestra "—" sin romper la fila.
    const rechazadaRow = screen.getByLabelText(/^validación de luis vendedor/i);
    expect(rechazadaRow.getAttribute('aria-label')).toMatch(/correo —/i);
  });

  it('AC5 (HU #11006, CF-03, regresión): Validaciones sigue listando ambos tipos, sin enviar standalone', async () => {
    renderValidaciones();

    await waitFor(() => {
      expect(mocks.listTenantBiometricPersons).toHaveBeenCalled();
    });
    const lastArg = mocks.listTenantBiometricPersons.mock.calls.at(-1)?.[0];
    expect(lastArg).not.toHaveProperty('standalone');
  });

  it('el motivo de rechazo no se pinta en la grilla agrupada: vive en el detalle de la persona', async () => {
    renderValidaciones();

    // La fila agrupada muestra el ESTADO de la validación más reciente; el motivo del rechazo es un
    // dato de esa validación concreta y se consulta abriendo el proceso de la persona.
    const row = await screen.findByLabelText(/^validación de luis vendedor/i);
    expect(within(row).getByText('Rechazado')).toBeInTheDocument();
    expect(
      screen.queryByText('La verificación del documento no fue exitosa.'),
    ).not.toBeInTheDocument();
  });

  it('muestra aprobación, expiración y días restantes de vigencia en la fila aprobada', async () => {
    renderValidaciones();

    // La fila aprobada expone la fecha de fin de vigencia y los días restantes (badge "20 días").
    const row = await screen.findByLabelText(/^validación de ana compradora/i);
    // Columnas desacopladas: la grilla expone cabeceras separadas Registro / Aprobación / Vigencia.
    expect(screen.getByText('Aprobación')).toBeInTheDocument();
    // "Vigencia" aparece como cabecera de columna Y como label del filtro → debe haber ≥1.
    expect(screen.getAllByText('Vigencia').length).toBeGreaterThan(0);
    // La fila aprobada muestra el badge de días restantes (en la columna Vigencia).
    expect(within(row).getByText('20 días')).toBeInTheDocument();
    // El aria-label resume la vigencia para lectores de pantalla.
    expect(row.getAttribute('aria-label')).toMatch(/vigente hasta/i);
    expect(row.getAttribute('aria-label')).toMatch(/vigencia: 20 días restantes/i);
  });

  it('la fila sin aprobación no muestra días de vigencia (—)', async () => {
    renderValidaciones();

    const row = await screen.findByLabelText(/^validación de luis vendedor/i);
    expect(within(row).queryByText(/día/)).not.toBeInTheDocument();
    expect(row.getAttribute('aria-label')).not.toMatch(/vigente hasta/i);
  });

  it('el botón Actualizar tiene nombre accesible', async () => {
    renderValidaciones();

    await screen.findByText('TRM-2026-000001');
    expect(
      screen.getByRole('button', { name: /actualizar validaciones de identidad/i }),
    ).toBeInTheDocument();
  });
});

/**
 * El admin FLIT necesita mirar las validaciones de UNA empresa a la vez (soporte). El tenant elegido
 * se fija en el cliente de trámites, que resuelve el header de toda la pantalla; al salir del módulo
 * se devuelve al tenant propio para no arrastrar la selección a Trámites o Dashboard.
 */
describe('Validaciones — selector de empresa del admin FLIT', () => {
  const EMPRESAS = {
    data: [
      { id: 'tenant-a', nit: '900', razonSocial: 'Movilidad Bogotá', estadoActivo: true },
      { id: 'tenant-b', nit: '901', razonSocial: 'Tránsito Medellín', estadoActivo: true },
    ],
  };

  it('un usuario de compañía no ve el selector', async () => {
    mocks.listTenantBiometricPersons.mockResolvedValue(FULL);

    renderValidaciones();
    await screen.findByText('TRM-2026-000001');

    expect(screen.queryByRole('combobox', { name: /ver las validaciones de otra empresa/i })).not.toBeInTheDocument();
    expect(mocks.listCompanies).not.toHaveBeenCalled();
  });

  it('el admin FLIT ve el selector con las empresas disponibles', async () => {
    mocks.isSuperAdmin.mockReturnValue(true);
    mocks.listCompanies.mockResolvedValue(EMPRESAS);
    mocks.listTenantBiometricPersons.mockResolvedValue(FULL);

    renderValidaciones();

    const user = userEvent.setup();
    const selector = await screen.findByRole('combobox', { name: /ver las validaciones de otra empresa/i });
    await user.click(selector);

    const lista = screen.getByRole('listbox');
    expect(within(lista).getByRole('option', { name: /Mi empresa/ })).toBeInTheDocument();
    expect(within(lista).getByRole('option', { name: /Movilidad Bogotá/ })).toBeInTheDocument();
  });

  it('se puede buscar la empresa por nombre en vez de recorrer la lista', async () => {
    const user = userEvent.setup();
    mocks.isSuperAdmin.mockReturnValue(true);
    mocks.listCompanies.mockResolvedValue(EMPRESAS);
    mocks.listTenantBiometricPersons.mockResolvedValue(FULL);

    renderValidaciones();
    await user.click(await screen.findByRole('combobox', { name: /ver las validaciones de otra empresa/i }));
    await user.keyboard('medell');

    const opciones = within(screen.getByRole('listbox')).getAllByRole('option');
    expect(opciones).toHaveLength(1);
    expect(opciones[0]).toHaveTextContent('Tránsito Medellín');
  });

  it('al escoger una empresa consulta sus validaciones con ese tenant', async () => {
    const user = userEvent.setup();
    mocks.isSuperAdmin.mockReturnValue(true);
    mocks.listCompanies.mockResolvedValue(EMPRESAS);
    mocks.listTenantBiometricPersons.mockResolvedValue(FULL);

    renderValidaciones();
    const selector = await screen.findByRole('combobox', { name: /ver las validaciones de otra empresa/i });
    mocks.listTenantBiometricPersons.mockClear();

    await user.click(selector);
    await user.click(screen.getByRole('option', { name: /Tránsito Medellín/ }));

    // El tenant se fija ANTES de recargar: la petición ya sale con la empresa elegida.
    expect(mocks.setActiveTramitesTenant).toHaveBeenCalledWith('tenant-b');
    await waitFor(() => expect(mocks.listTenantBiometricPersons).toHaveBeenCalled());
  });

  it('volver a "Mi empresa" quita el override', async () => {
    const user = userEvent.setup();
    mocks.isSuperAdmin.mockReturnValue(true);
    mocks.listCompanies.mockResolvedValue(EMPRESAS);
    mocks.listTenantBiometricPersons.mockResolvedValue(FULL);

    renderValidaciones();
    const selector = await screen.findByRole('combobox', { name: /ver las validaciones de otra empresa/i });
    await user.click(selector);
    await user.click(screen.getByRole('option', { name: /Movilidad Bogotá/ }));
    await user.click(selector);
    await user.click(screen.getByRole('option', { name: /Mi empresa/ }));

    expect(mocks.setActiveTramitesTenant).toHaveBeenLastCalledWith(undefined);
  });

  it('al salir del módulo se devuelve el cliente al tenant propio', async () => {
    mocks.isSuperAdmin.mockReturnValue(true);
    mocks.listCompanies.mockResolvedValue(EMPRESAS);
    mocks.listTenantBiometricPersons.mockResolvedValue(FULL);

    const { unmount } = renderValidaciones();
    await screen.findByRole('combobox', { name: /ver las validaciones de otra empresa/i });
    mocks.setActiveTramitesTenant.mockClear();

    unmount();

    expect(mocks.setActiveTramitesTenant).toHaveBeenCalledWith(undefined);
  });

  it('si falla el listado de empresas la pantalla sigue funcionando, sin selector', async () => {
    mocks.isSuperAdmin.mockReturnValue(true);
    mocks.listCompanies.mockRejectedValue(new Error('503'));
    mocks.listTenantBiometricPersons.mockResolvedValue(FULL);

    renderValidaciones();

    expect(await screen.findByText('TRM-2026-000001')).toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: /ver las validaciones de otra empresa/i })).not.toBeInTheDocument();
  });
});

describe('Validaciones — filtros (HU #10348)', () => {
  it('filtra por estado: al elegir un valor re-consulta el backend con ese estado', async () => {
    const user = userEvent.setup();
    mocks.listTenantBiometricPersons.mockResolvedValue(FULL);

    renderValidaciones();
    await screen.findByText('TRM-2026-000001'); // carga inicial

    await user.selectOptions(screen.getByLabelText('Estado'), 'aprobado');

    await waitFor(() =>
      expect(mocks.listTenantBiometricPersons).toHaveBeenCalledWith(
        expect.objectContaining({ status: 'aprobado' }),
      ),
    );
  });

  it('filtra por vigencia: al elegir "Por vencer" re-consulta con vigenciaEstado', async () => {
    const user = userEvent.setup();
    mocks.listTenantBiometricPersons.mockResolvedValue(FULL);

    renderValidaciones();
    await screen.findByText('TRM-2026-000001');

    await user.selectOptions(screen.getByLabelText('Vigencia'), 'por_vencer');

    await waitFor(() =>
      expect(mocks.listTenantBiometricPersons).toHaveBeenCalledWith(
        expect.objectContaining({ vigenciaEstado: 'por_vencer' }),
      ),
    );
  });

  it('filtra por rango de expiración: "Vence desde/hasta" viajan como expiraDesde/expiraHasta', async () => {
    const user = userEvent.setup();
    mocks.listTenantBiometricPersons.mockResolvedValue(FULL);

    renderValidaciones();
    await screen.findByText('TRM-2026-000001');

    // Los date-pickers de vencimiento viven en el panel avanzado.
    await user.click(screen.getByRole('button', { name: /más filtros/i }));
    await user.type(screen.getByLabelText('Vence desde'), '2026-07-01');
    await user.type(screen.getByLabelText('Vence hasta'), '2026-07-31');

    await waitFor(() =>
      expect(mocks.listTenantBiometricPersons).toHaveBeenCalledWith(
        expect.objectContaining({
          expiraDesde: '2026-07-01T00:00:00',
          expiraHasta: '2026-07-31T23:59:59',
        }),
      ),
    );
  });

  it('filtra por "Vence en ≤ N días": el número viaja como venceEnDias', async () => {
    const user = userEvent.setup();
    mocks.listTenantBiometricPersons.mockResolvedValue(FULL);

    renderValidaciones();
    await screen.findByText('TRM-2026-000001');

    await user.click(screen.getByRole('button', { name: /más filtros/i }));
    await user.type(screen.getByLabelText('Vence en ≤ N días'), '3');

    await waitFor(() =>
      expect(mocks.listTenantBiometricPersons).toHaveBeenCalledWith(
        expect.objectContaining({ venceEnDias: 3 }),
      ),
    );
  });

  it('combina filtros de texto (nombre + documento) y los envía al backend', async () => {
    const user = userEvent.setup();
    mocks.listTenantBiometricPersons.mockResolvedValue(FULL);

    renderValidaciones();
    await screen.findByLabelText(/filtrar por nombre de la persona/i);

    await user.type(screen.getByLabelText(/filtrar por nombre de la persona/i), 'Ana');
    await user.type(screen.getByLabelText('Documento'), '1020304050');

    // Debounce ~300ms; la última consulta combina ambos filtros (AND en el backend).
    await waitFor(() =>
      expect(mocks.listTenantBiometricPersons).toHaveBeenCalledWith(
        expect.objectContaining({ name: 'Ana', documentNumber: '1020304050' }),
      ),
    );
  });

  it('la grilla por persona NO ofrece filtros de una validación concreta', async () => {
    mocks.listTenantBiometricPersons.mockResolvedValue(FULL);

    renderValidaciones();
    await screen.findByText('TRM-2026-000001');

    // Trámite, modalidad, parte, proveedor y score describen UNA validación, no a la persona:
    // se consultan en el detalle, no aquí.
    expect(screen.queryByLabelText(/filtrar por número de trámite/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Modalidad')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Parte')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Proveedor')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Score mín.')).not.toBeInTheDocument();
  });

  it('sin resultados: con filtros activos y respuesta vacía muestra "Sin resultados"', async () => {
    const user = userEvent.setup();
    mocks.listTenantBiometricPersons
      .mockResolvedValueOnce(FULL) // carga inicial con datos
      .mockResolvedValue(EMPTY); // tras filtrar, sin coincidencias

    renderValidaciones();
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
    mocks.listTenantBiometricPersons.mockResolvedValue(FULL);

    renderValidaciones();
    await screen.findByText('TRM-2026-000001');

    const estadoSelect = screen.getByLabelText('Estado');
    await user.selectOptions(estadoSelect, 'aprobado');
    await waitFor(() => expect(estadoSelect).toHaveValue('aprobado'));

    mocks.listTenantBiometricPersons.mockClear();
    await user.click(screen.getByRole('button', { name: /limpiar filtros/i }));

    await waitFor(() => {
      expect(mocks.listTenantBiometricPersons).toHaveBeenCalled();
      const lastArg = mocks.listTenantBiometricPersons.mock.calls.at(-1)?.[0];
      expect(lastArg?.status).toBeUndefined();
    });
    // El control vuelve a "Todos" (valor vacío).
    expect(screen.getByLabelText('Estado')).toHaveValue('');
  });

  it('auto-refresca la grilla en vivo por intervalo (suscripción), sin pulsar Actualizar', async () => {
    vi.useFakeTimers();
    try {
      mocks.listTenantBiometricPersons.mockResolvedValue(FULL);
      render(<Validaciones />);

      // Resuelve la carga inicial. Con timers falsos se usa act + advanceTimers: userEvent
      // espera timers reales para su delay interno.
      await act(async () => {
        await vi.advanceTimersByTimeAsync(0);
      });
      expect(mocks.listTenantBiometricPersons).toHaveBeenCalledTimes(1);

      // Al cumplirse el intervalo de auto-refresco, vuelve a consultar el backend sin interacción.
      await act(async () => {
        await vi.advanceTimersByTimeAsync(15_000);
      });
      expect(mocks.listTenantBiometricPersons).toHaveBeenCalledTimes(2);

      // La consulta automática se hace en segundo plano (último parámetro de filtros, sin query).
      const lastArg = mocks.listTenantBiometricPersons.mock.calls.at(-1)?.[0];
      expect(lastArg?.status).toBeUndefined();
    } finally {
      vi.useRealTimers();
    }
  });
});

describe('Validaciones — eventos atascados (dead-letter, HU #10349 / #11268)', () => {
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

  async function expandStuckGroup(label: RegExp | string) {
    const banner = await screen.findByRole('region', {
      name: /validaciones de identidad atascadas/i,
    });
    const toggle = within(banner).getByRole('button', {
      name: typeof label === 'string' ? new RegExp(label, 'i') : label,
    });
    await userEvent.click(toggle);
    return banner;
  }

  it('muestra el banner con el nombre y el documento enmascarado de la persona', async () => {
    mocks.listTenantBiometricPersons.mockResolvedValue(FULL);
    mocks.listStuckIdentityValidations.mockResolvedValue(STUCK);

    renderValidaciones();

    expect(await screen.findByText(/de identidad atascada/i)).toBeInTheDocument();
    const banner = await expandStuckGroup(/maria compradora/i);
    expect(within(banner).getAllByText(/Maria Compradora/).length).toBeGreaterThanOrEqual(1);
    expect(within(banner).getByText(/CC ••••4050/)).toBeInTheDocument();
    expect(within(banner).queryByText(/val-12345678/)).not.toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /reintentar la validación de maria compradora/i }),
    ).toBeInTheDocument();
  });

  it('etiqueta cada fila según la cola atascada (envío al proveedor vs. firma·FUR)', async () => {
    mocks.listTenantBiometricPersons.mockResolvedValue(FULL);
    mocks.listStuckIdentityValidations.mockResolvedValue({
      stuck: [
        { ...STUCK.stuck[0], id: 'evt-envio', kind: 'envio', name: 'Pedro Envio' },
        {
          ...STUCK.stuck[0],
          id: 'evt-cad',
          validationId: 'val-bbbbbbbb',
          kind: 'encadenamiento',
          name: 'Ana Cadena',
          documentNumber: '9999',
        },
      ],
      total: 2,
      maxDeliveryAttempts: 5,
    });

    renderValidaciones();

    const banner = await screen.findByRole('region', {
      name: /validaciones de identidad atascadas/i,
    });
    await userEvent.click(within(banner).getByRole('button', { name: /pedro envio/i }));
    await userEvent.click(within(banner).getByRole('button', { name: /ana cadena/i }));
    expect(within(banner).getByText('Envío a proveedor')).toBeInTheDocument();
    expect(within(banner).getByText('Firma · FUR')).toBeInTheDocument();
  });

  it('Reintentar reencola el evento en el backend y el banner desaparece al refrescar', async () => {
    const user = userEvent.setup();
    mocks.listTenantBiometricPersons.mockResolvedValue(FULL);
    mocks.listStuckIdentityValidations
      .mockResolvedValueOnce(STUCK) // carga inicial: hay un atascado
      .mockResolvedValue(NO_STUCK); // tras reencolar: ya no

    renderValidaciones();
    await expandStuckGroup(/maria compradora/i);
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
    mocks.listTenantBiometricPersons.mockResolvedValue(FULL);
    // listStuckIdentityValidations usa el default NO_STUCK del beforeEach.

    renderValidaciones();

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
    mocks.listTenantBiometricPersons.mockResolvedValue(FULL);
    mocks.listStuckIdentityValidations
      .mockResolvedValueOnce(STUCK_MANY) // hay 2 atascados
      .mockResolvedValue(NO_STUCK); // tras reencolar todos: ninguno

    renderValidaciones();
    const btn = await screen.findByRole('button', {
      name: /reintentar todas las validaciones atascadas/i,
    });

    await user.click(btn);

    await waitFor(() => expect(mocks.requeueAllStuckIdentityValidations).toHaveBeenCalled());
    await waitFor(() =>
      expect(screen.queryByText(/de identidad atascada/i)).not.toBeInTheDocument(),
    );
  });

  // HU #11268 — acordeón por persona
  it('agrupa por persona en grupos colapsados con contador (AC1)', async () => {
    mocks.listTenantBiometricPersons.mockResolvedValue(FULL);
    mocks.listStuckIdentityValidations.mockResolvedValue({
      stuck: [
        { ...STUCK.stuck[0], id: 'e1' },
        { ...STUCK.stuck[0], id: 'e2', validationId: 'v2' },
        { ...STUCK.stuck[0], id: 'e3', validationId: 'v3' },
        {
          id: 'e4',
          validationId: 'v4',
          eventType: 'identity_validation.completed',
          attempts: 5,
          occurredAt: '2026-06-20T12:00:00Z',
          createdAt: '2026-06-20T12:00:00Z',
          name: 'Luis Solo',
          documentType: 'CC',
          documentNumber: '1111',
        },
      ],
      total: 4,
      maxDeliveryAttempts: 5,
    });

    renderValidaciones();
    const banner = await screen.findByRole('region', {
      name: /validaciones de identidad atascadas/i,
    });
    const maria = within(banner).getByRole('button', { name: /maria compradora.*3 eventos/i });
    const luis = within(banner).getByRole('button', { name: /luis solo.*1 evento/i });
    expect(maria).toHaveAttribute('aria-expanded', 'false');
    expect(luis).toHaveAttribute('aria-expanded', 'false');
    // Filas no visibles mientras está colapsado
    expect(
      within(banner).queryByRole('button', { name: /reintentar la validación de maria/i }),
    ).not.toBeInTheDocument();
  });

  it('conserva Reintentar por fila y Reintentar todos al expandir (AC2)', async () => {
    const user = userEvent.setup();
    mocks.listTenantBiometricPersons.mockResolvedValue(FULL);
    mocks.listStuckIdentityValidations.mockResolvedValue({
      stuck: [
        { ...STUCK.stuck[0], id: 'e1' },
        {
          id: 'e2',
          validationId: 'v2',
          eventType: 'identity_validation.completed',
          attempts: 5,
          occurredAt: '2026-06-20T11:00:00Z',
          createdAt: '2026-06-20T11:00:00Z',
          name: 'Otra Persona',
          documentType: 'CC',
          documentNumber: '2222',
        },
      ],
      total: 2,
      maxDeliveryAttempts: 5,
    });

    renderValidaciones();
    const banner = await screen.findByRole('region', {
      name: /validaciones de identidad atascadas/i,
    });
    expect(
      within(banner).getByRole('button', { name: /reintentar todas las validaciones atascadas/i }),
    ).toBeInTheDocument();
    await user.click(within(banner).getByRole('button', { name: /maria compradora/i }));
    expect(
      within(banner).getByRole('button', { name: /reintentar la validación de maria compradora/i }),
    ).toBeInTheDocument();
  });

  it('agrupa eventos sin nombre ni documento como No identificados (AC3)', async () => {
    mocks.listTenantBiometricPersons.mockResolvedValue(FULL);
    mocks.listStuckIdentityValidations.mockResolvedValue({
      stuck: [
        { ...STUCK.stuck[0], id: 'e1' },
        {
          id: 'e-orphan',
          validationId: 'v-gone',
          eventType: 'identity_validation.completed',
          attempts: 5,
          occurredAt: '2026-06-20T11:00:00Z',
          createdAt: '2026-06-20T11:00:00Z',
          name: null,
          documentType: null,
          documentNumber: null,
        },
      ],
      total: 2,
      maxDeliveryAttempts: 5,
    });

    renderValidaciones();
    const banner = await screen.findByRole('region', {
      name: /validaciones de identidad atascadas/i,
    });
    expect(within(banner).getByRole('button', { name: /no identificados/i })).toBeInTheDocument();
    expect(within(banner).getByRole('button', { name: /maria compradora/i })).toBeInTheDocument();
  });

  it('muestra estado de carga y de error del panel de atascadas (AC4)', async () => {
    mocks.listTenantBiometricPersons.mockResolvedValue(FULL);
    let resolveStuck!: (v: typeof NO_STUCK) => void;
    mocks.listStuckIdentityValidations.mockImplementation(
      () =>
        new Promise((resolve) => {
          resolveStuck = resolve;
        }),
    );

    const { unmount } = renderValidaciones();
    expect(screen.getByRole('status', { name: /cargando validaciones atascadas/i })).toBeInTheDocument();
    resolveStuck(NO_STUCK);
    await waitFor(() =>
      expect(screen.queryByRole('status', { name: /cargando validaciones atascadas/i })).not.toBeInTheDocument(),
    );
    unmount();

    mocks.listStuckIdentityValidations.mockRejectedValue(new Error('cola caída'));
    renderValidaciones();
    expect(
      await screen.findByRole('alert', { name: /error al cargar validaciones atascadas/i }),
    ).toBeInTheDocument();
    expect(screen.getByText(/cola caída/i)).toBeInTheDocument();
  });
});

describe('Validaciones — paginación', () => {
  const PAGED: TenantBiometricPersonsResponse = {
    ...FULL,
    total: 45, // 45 / 20 = 3 páginas
  };

  it('navega a la página siguiente y consulta el backend con esa página', async () => {
    const user = userEvent.setup();
    mocks.listTenantBiometricPersons.mockResolvedValue(PAGED);

    renderValidaciones();
    await screen.findByText('TRM-2026-000001');

    expect(screen.getByText(/página 1 de 3/i)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /página siguiente/i }));

    await waitFor(() =>
      expect(mocks.listTenantBiometricPersons).toHaveBeenCalledWith(
        expect.objectContaining({ page: 2, pageSize: 20 }),
      ),
    );
  });

  it('cambiar filas por página vuelve a la página 1 con el nuevo tamaño', async () => {
    const user = userEvent.setup();
    mocks.listTenantBiometricPersons.mockResolvedValue(PAGED);

    renderValidaciones();
    await screen.findByText('TRM-2026-000001');

    await user.selectOptions(screen.getByLabelText('Filas por página'), '50');

    await waitFor(() =>
      expect(mocks.listTenantBiometricPersons).toHaveBeenCalledWith(
        expect.objectContaining({ page: 1, pageSize: 50 }),
      ),
    );
  });

  // CF-05 (HU #10886, AC2) — el módulo de identidad muestra el enlace vigente para reenviarlo por
  // otros medios, con su estado y su expiración.
  describe('enlace de validación vigente', () => {
    it('ofrece "Copiar enlace" con la expiración en las validaciones en curso', async () => {
      const user = userEvent.setup();
      mocks.listTenantBiometricPersons.mockResolvedValue(personsResponse([ROW_EN_PROCESO]));

      renderValidaciones();
      await screen.findByText('TRM-2026-000003');

      expect(screen.queryByText('No disponible')).not.toBeInTheDocument();
      await user.click(screen.getByRole('button', { name: /acciones de validación de carlos vendedor/i }));
      expect(screen.getByRole('menuitem', { name: /copiar enlace/i })).toBeInTheDocument();
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
      mocks.listTenantBiometricPersons.mockResolvedValue(personsResponse([ROW_EN_PROCESO]));

      renderValidaciones();
      await screen.findByText('TRM-2026-000003');

      await user.click(screen.getByRole('button', { name: /acciones de validación de carlos vendedor/i }));
      await user.click(screen.getByRole('menuitem', { name: /copiar enlace/i }));

      expect(writeText).toHaveBeenCalledWith('https://capture.kyverum.co/kyv_123');
      expect(await screen.findByText('Enlace copiado')).toBeInTheDocument();
    });

    it('no ofrece enlace cuando la validación ya está en estado terminal', async () => {
      const user = userEvent.setup();
      mocks.listTenantBiometricPersons.mockResolvedValue(personsResponse([ROW_APROBADA]));

      renderValidaciones();
      await screen.findByText('TRM-2026-000001');

      await user.click(screen.getByRole('button', { name: /acciones de validación de ana compradora/i }));
      expect(screen.queryByRole('menuitem', { name: /copiar enlace/i })).not.toBeInTheDocument();
    });

    it('la columna "Enlace vigente" muestra la fecha CON HORA, sin la palabra "Vence"', async () => {
      mocks.listTenantBiometricPersons.mockResolvedValue(personsResponse([ROW_EN_PROCESO]));

      renderValidaciones();
      await screen.findByText('TRM-2026-000003');

      expect(screen.getByText('Enlace vigente')).toBeInTheDocument();
      // "medium + short" incluye la hora: el gestor necesita saber a qué hora deja de servir.
      const esperado = new Intl.DateTimeFormat('es-CO', {
        dateStyle: 'medium',
        timeStyle: 'short',
      }).format(new Date(LINK_FUTURO));
      const grilla = screen.getByRole('list', { name: /validaciones de identidad/i });
      expect(within(grilla).getByText(esperado)).toBeInTheDocument();
      // Sin "Vence" delante: el nombre de la columna ya lo dice (el filtro "Por vencer" no cuenta).
      expect(within(grilla).queryByText(/vence/i)).not.toBeInTheDocument();
    });

    it('terminada: la celda Enlace dice "No disponible" aunque el backend siga devolviendo la URL', async () => {
      const user = userEvent.setup();
      // El endpoint agrupado NO anula captureUrl en estados terminales: el enlace llega pero no sirve.
      mocks.listTenantBiometricPersons.mockResolvedValue(
        personsResponse([
          {
            ...ROW_APROBADA,
            captureUrl: 'https://capture.kyverum.co/kyv_viejo',
            linkExpiresAt: LINK_FUTURO,
          },
        ]),
      );

      renderValidaciones();
      await screen.findByText('TRM-2026-000001');

      expect(screen.getByText('No disponible')).toBeInTheDocument();
      await user.click(screen.getByRole('button', { name: /acciones de validación/i }));
      expect(screen.queryByRole('menuitem', { name: /copiar enlace/i })).not.toBeInTheDocument();
      expect(screen.queryByRole('menuitem', { name: /abrir captura/i })).not.toBeInTheDocument();
    });

    it('en proceso con el enlace vencido: la fila se lee "Expirado", como lo cuenta el KPI', async () => {
      // El backend puede seguir teniendo status='en_proceso' mientras el worker no la marca; `expired`
      // ya aplica la regla real. La fila debe decir lo mismo que el contador, o no cuadran.
      mocks.listTenantBiometricPersons.mockResolvedValue(
        personsResponse([{ ...ROW_EN_PROCESO, expired: true, linkExpiresAt: LINK_VENCIDO }]),
      );

      renderValidaciones();
      await screen.findByText('TRM-2026-000003');

      const fila = screen.getByRole('list', { name: /validaciones de identidad/i });
      expect(within(fila).getByText('Expirado')).toBeInTheDocument();
      expect(within(fila).queryByText('En proceso')).not.toBeInTheDocument();
    });

    it('en vuelo pero con el enlace ya vencido: tampoco es utilizable', async () => {
      const user = userEvent.setup();
      mocks.listTenantBiometricPersons.mockResolvedValue(
        personsResponse([{ ...ROW_EN_PROCESO, linkExpiresAt: LINK_VENCIDO }]),
      );

      renderValidaciones();
      await screen.findByText('TRM-2026-000003');

      expect(screen.getByText('No disponible')).toBeInTheDocument();
      await user.click(screen.getByRole('button', { name: /acciones de validación/i }));
      expect(screen.queryByRole('menuitem', { name: /copiar enlace/i })).not.toBeInTheDocument();
    });
  });

  /**
   * Reenviar invalida el enlace anterior: no tiene sentido cuando la identidad ya está aprobada
   * (no hay nada que capturar) ni mientras la persona está EN PROCESO (le rompería la sesión abierta).
   */
  describe('cuándo se ofrece Reenviar', () => {
    /** Prevalidación standalone (sin trámite): es la única gestionable desde esta pantalla. */
    const STANDALONE: TenantBiometricValidation = {
      ...ROW_EN_PROCESO,
      id: 'pv-x',
      instanceId: null,
      referenceNumber: null,
      modalidad: null,
      partyRole: null,
      name: 'Sara Standalone',
    };

    async function abrirAcciones(row: TenantBiometricValidation) {
      const user = userEvent.setup();
      mocks.listTenantBiometricPersons.mockResolvedValue(personsResponse([row]));
      renderValidaciones();
      await screen.findByText(row.name);
      await user.click(screen.getByRole('button', { name: /acciones de validación/i }));
    }

    it('en proceso: NO ofrece Reenviar (el enlace vigente ya le sirve a la persona)', async () => {
      await abrirAcciones({ ...STANDALONE, status: 'en_proceso' });

      expect(screen.queryByRole('menuitem', { name: /^reenviar$/i })).not.toBeInTheDocument();
      // El enlace sigue disponible para compartirlo por otro medio.
      expect(screen.getByRole('menuitem', { name: /copiar enlace/i })).toBeInTheDocument();
    });

    it('aprobada: NO ofrece Reenviar, ofrece crear una prevalidación nueva', async () => {
      await abrirAcciones({ ...STANDALONE, status: 'aprobado', captureUrl: null });

      expect(screen.queryByRole('menuitem', { name: /^reenviar$/i })).not.toBeInTheDocument();
      expect(screen.getByRole('menuitem', { name: /nueva prevalidación/i })).toBeInTheDocument();
    });

    it.each(['enviado', 'rechazado', 'expirado', 'error_envio'] as const)(
      '%s: SÍ ofrece Reenviar',
      async (status) => {
        await abrirAcciones({ ...STANDALONE, status, captureUrl: null });

        expect(screen.getByRole('menuitem', { name: /^reenviar$/i })).toBeInTheDocument();
      },
    );

    it('expirada standalone: Reenviar habilitado (es el caso para el que existe)', async () => {
      await abrirAcciones({ ...STANDALONE, status: 'expirado', expired: true, captureUrl: null });

      const reenviar = screen.getByRole('menuitem', { name: /^reenviar$/i });
      expect(reenviar).toBeInTheDocument();
      expect(reenviar).not.toBeDisabled();
    });

    it('rechazada de un trámite: "Reenviar" lanza una validación nueva (sin ella el trámite se atasca)', async () => {
      const user = userEvent.setup();
      mocks.iniciarBiometric.mockResolvedValue({ validation: { id: 'val-nueva' } });
      await abrirAcciones({ ...ROW_EN_PROCESO, status: 'rechazado', captureUrl: null });

      // Mismo rótulo que en las standalone, pero por dentro no es un reenvío por id (el backend lo
      // prohíbe en validaciones de trámite): lanza una validación nueva para la parte.
      expect(screen.queryByRole('menuitem', { name: /^editar$/i })).not.toBeInTheDocument();
      await user.click(screen.getByRole('menuitem', { name: /^reenviar$/i }));
      await user.click(screen.getByRole('button', { name: /confirmar reenvío/i }));

      await waitFor(() =>
        expect(mocks.iniciarBiometric).toHaveBeenCalledWith('inst-3', { parte: 'vendedor' }),
      );
      expect(mocks.resendPrevalidacion).not.toHaveBeenCalled();
    });

    it('vencida de un trámite: mismo rótulo "Reenviar", sin vocabulario aparte', async () => {
      await abrirAcciones({ ...ROW_EN_PROCESO, status: 'expirado', expired: true, captureUrl: null });

      expect(screen.getByRole('menuitem', { name: /^reenviar$/i })).toBeInTheDocument();
      expect(screen.queryByRole('menuitem', { name: /reiniciar|reintentar/i })).not.toBeInTheDocument();
    });

    it('trámite ya radicado: el 409 explica que hay que abrir una subsanación', async () => {
      const user = userEvent.setup();
      const { TramitesApiError } = await import('@/lib/api/tramites-client');
      mocks.iniciarBiometric.mockRejectedValueOnce(
        new TramitesApiError(409, 'Solo se puede iniciar biométrica en borrador o con subsanación activa.', null),
      );
      await abrirAcciones({ ...ROW_EN_PROCESO, status: 'rechazado', captureUrl: null });

      await user.click(screen.getByRole('menuitem', { name: /^reenviar$/i }));
      await user.click(screen.getByRole('button', { name: /confirmar reenvío/i }));

      expect(await screen.findByRole('alert')).toHaveTextContent(/abre una subsanación/i);
    });

    it('en proceso de un trámite: no ofrece reintentar (el enlace vigente sigue sirviendo)', async () => {
      await abrirAcciones(ROW_EN_PROCESO);

      expect(screen.queryByRole('menuitem', { name: /reintentar|reiniciar/i })).not.toBeInTheDocument();
      expect(screen.getByRole('menuitem', { name: /copiar enlace/i })).toBeInTheDocument();
    });

    it('ya no se pinta el candado de solo lectura en las filas de trámite', async () => {
      mocks.listTenantBiometricPersons.mockResolvedValue(personsResponse([ROW_APROBADA]));

      renderValidaciones();
      await screen.findByText('TRM-2026-000001');

      expect(screen.queryByText(/solo lectura/i)).not.toBeInTheDocument();
    });
  });
});

// HU #11007/#11008 — proceso en panel lateral (tabla compacta).
describe('Validaciones — proceso de identidad (HU #11007/#11008)', () => {
  it('AC1: "Ver proceso" abre el historial de LA PERSONA (todas sus validaciones y sus bitácoras)', async () => {
    const user = userEvent.setup();
    mocks.listTenantBiometricPersons.mockResolvedValue(FULL);
    // El detalle trae el desglose por validación: es ahí donde vive lo que ya no se repite en la grilla.
    mocks.listPersonBiometricValidations.mockResolvedValue({
      documentType: ROW_APROBADA.documentType,
      documentNumber: ROW_APROBADA.documentNumber,
      name: ROW_APROBADA.name,
      validations: [
        {
          id: ROW_APROBADA.id,
          partyRole: ROW_APROBADA.partyRole,
          name: ROW_APROBADA.name,
          documentType: ROW_APROBADA.documentType,
          documentNumber: ROW_APROBADA.documentNumber,
          email: ROW_APROBADA.email,
          status: ROW_APROBADA.status,
          intentos: 1,
          maxIntentos: 3,
          score: ROW_APROBADA.score,
          expiresAt: '2026-07-28T10:00:00Z',
          validatedAt: ROW_APROBADA.validatedAt,
          expired: false,
          provider: ROW_APROBADA.provider,
          captureUrl: null,
        },
      ],
      page: 1,
      pageSize: 50,
      total: 1,
      allTerminal: true,
    });
    mocks.getBiometricAuditByValidation.mockResolvedValue({
      validationId: ROW_APROBADA.id,
      events: [],
      referencedFromOtherProcedure: false,
    });

    renderValidaciones();
    await screen.findByText('TRM-2026-000001');

    const [primerMenu] = screen.getAllByRole('button', { name: /acciones de validación/i });
    await user.click(primerMenu);
    await user.click(screen.getByRole('menuitem', { name: /ver proceso/i }));

    expect(await screen.findByRole('dialog', { name: /proceso de validación/i })).toBeInTheDocument();
    await waitFor(() =>
      expect(mocks.listPersonBiometricValidations).toHaveBeenCalledWith(
        ROW_APROBADA.documentType,
        ROW_APROBADA.documentNumber,
        expect.anything(),
      ),
    );
  });

  it('HU #11069 — en el listado de Validaciones NO embebe trámites (van en el detalle)', async () => {
    mocks.listTenantBiometricPersons.mockResolvedValue(
      personsResponse([
        {
          ...ROW_APROBADA,
          instanceId: null,
          referenceNumber: null,
          modalidad: null,
          linkedProcedures: [
            {
              instanceId: 'inst-99',
              referenceNumber: 'TRM-2026-000099',
              status: 'borrador',
              modalidad: 'matricula_inicial',
            },
          ],
        },
      ]),
    );

    renderValidaciones();
    expect(await screen.findByText('Prevalidación')).toBeInTheDocument();
    expect(screen.queryByLabelText(/trámites asociados|otros trámites vinculados/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/Trámites:/i)).not.toBeInTheDocument();
  });
});
