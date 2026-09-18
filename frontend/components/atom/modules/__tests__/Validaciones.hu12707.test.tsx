/**
 * HU #12707 — Validación de Identidad con la barra de Trámites y la compañía de cada registro.
 * Vitest + RTL, con el cliente de trámites simulado: se afirma sobre la PETICIÓN (parámetros y compañía)
 * y sobre lo que ve el usuario.
 */
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const mocks = vi.hoisted(() => ({
  isSuperAdmin: false,
  listTenantBiometricPersons: vi.fn(),
  listStuckIdentityValidations: vi.fn(),
  listPersonBiometricValidations: vi.fn(),
  requeueStuckIdentityValidation: vi.fn(),
  requeueAllStuckIdentityValidations: vi.fn(),
  setActiveTramitesTenant: vi.fn(),
  listCompanies: vi.fn(),
}));

vi.mock('@/lib/api/superadmin-client', () => ({
  superadminClient: { listCompanies: mocks.listCompanies },
}));
vi.mock('@/lib/api/client', () => ({ getToken: () => 'token' }));
vi.mock('@/lib/auth/jwt', () => ({
  decodeJwtPayload: () => ({}),
  isSuperAdmin: () => mocks.isSuperAdmin,
  hasPermission: () => false,
}));
vi.mock('@/lib/api/ui-preferences', () => ({
  uiPreferencesClient: {
    get: vi.fn().mockResolvedValue({ scope: 'identidad.columns', value: {} }),
    put: vi.fn().mockImplementation((_scope: string, value: unknown) => Promise.resolve({ scope: 'identidad.columns', value })),
  },
}));
vi.mock('@/lib/api/tramites-client', () => ({
  ALL_TENANTS: '*',
  tramitesClient: {
    listTenantBiometricPersons: mocks.listTenantBiometricPersons,
    listStuckIdentityValidations: mocks.listStuckIdentityValidations,
    listPersonBiometricValidations: mocks.listPersonBiometricValidations,
    requeueStuckIdentityValidation: mocks.requeueStuckIdentityValidation,
    requeueAllStuckIdentityValidations: mocks.requeueAllStuckIdentityValidations,
  },
  setActiveTramitesTenant: mocks.setActiveTramitesTenant,
  TramitesApiError: class TramitesApiError extends Error {},
  getIdentitySendConflict: () => null,
}));

import { ToastProvider } from '@/components/admin/Toast';
import { Validaciones } from '@/components/atom/modules/Validaciones';
import type { TenantBiometricPerson, TenantBiometricPersonsResponse } from '@/lib/api/types/procedure-runtime';

const TENANT_A = 'aaaaaaaa-0000-4000-8000-00000000000a';
const TENANT_B = 'bbbbbbbb-0000-4000-8000-00000000000b';

function persona(overrides: Partial<TenantBiometricPerson>): TenantBiometricPerson {
  return {
    documentType: 'CC',
    documentNumber: '1020445118',
    name: 'Carolina Pérez',
    status: 'aprobado',
    validationCount: 1,
    worstAlertKind: null,
    latestValidationId: 'val-1',
    instanceId: null,
    referenceNumber: null,
    modalidad: null,
    partyRole: null,
    email: 'carolina@correo.co',
    provider: 'kyverum',
    score: 91,
    captureUrl: null,
    expired: false,
    intentos: 1,
    maxIntentos: 3,
    createdAt: '2026-09-10T10:00:00Z',
    validatedAt: '2026-09-10T10:05:00Z',
    validUntil: '2026-10-10T10:05:00Z',
    daysRemaining: 22,
    linkExpiresAt: null,
    ...overrides,
  } as TenantBiometricPerson;
}

function respuesta(persons: TenantBiometricPerson[]): TenantBiometricPersonsResponse {
  return {
    persons,
    stats: { total: persons.length, aprobadas: persons.length, enProceso: 0, rechazadas: 0, expiradas: 0 },
    page: 1,
    pageSize: 10,
    total: persons.length,
  };
}

function renderValidaciones() {
  return render(
    <ToastProvider>
      <Validaciones />
    </ToastProvider>,
  );
}

/** Filtros de la ÚLTIMA petición del listado. */
const ultimaPeticion = () => mocks.listTenantBiometricPersons.mock.calls.at(-1)!;

beforeEach(() => {
  vi.clearAllMocks();
  mocks.isSuperAdmin = false;
  try {
    sessionStorage.clear();
  } catch {
    /* sin almacenamiento */
  }
  mocks.listStuckIdentityValidations.mockResolvedValue({ stuck: [], total: 0, maxDeliveryAttempts: 5 });
  mocks.listCompanies.mockResolvedValue({
    data: [
      { id: TENANT_A, nit: '900111222', razonSocial: 'Renting Andino SAS', estadoActivo: true },
      { id: TENANT_B, nit: '800333444', razonSocial: 'Concesionario Bolívar SAS', estadoActivo: true },
    ],
  });
});

describe('SuperAdmin — todas las compañías (AC5–AC7)', () => {
  beforeEach(() => {
    mocks.isSuperAdmin = true;
    mocks.listTenantBiometricPersons.mockResolvedValue(
      respuesta([
        persona({ tenantId: TENANT_A, tenantName: 'Renting Andino SAS', latestValidationId: 'val-a' }),
        persona({ tenantId: TENANT_B, tenantName: 'Concesionario Bolívar SAS', latestValidationId: 'val-b', name: 'Carolina Pérez B' }),
      ]),
    );
  });

  it('arranca en «Todas»: pide sin compañía y la primera columna es Compañía con razón social y NIT', async () => {
    renderValidaciones();

    await screen.findByText('Carolina Pérez B');
    expect(mocks.listTenantBiometricPersons).toHaveBeenCalledWith(expect.any(Object), '*');
    expect(mocks.listStuckIdentityValidations).toHaveBeenCalledWith('*');
    // Cabecera: Compañía primero.
    const filas = screen.getByRole('list', { name: /validaciones de identidad/i });
    expect(within(filas).getByText('Renting Andino SAS')).toBeInTheDocument();
    expect(await screen.findByText('NIT 800333444')).toBeInTheDocument();
    expect(screen.getAllByText('Compañía')[0]).toBeInTheDocument();
  });

  it('al elegir una compañía, el listado y las incidencias se acotan a ella', async () => {
    const user = userEvent.setup();
    renderValidaciones();
    await screen.findByText('Carolina Pérez B');

    await user.click(await screen.findByRole('combobox', { name: 'Compañía' }));
    await user.click(await screen.findByRole('option', { name: /Concesionario Bolívar/ }));

    await waitFor(() => expect(ultimaPeticion()[1]).toBe(TENANT_B));
    expect(mocks.listStuckIdentityValidations).toHaveBeenLastCalledWith(TENANT_B);
  });

  it('AC6 — «Ver proceso» de una fila abre el detalle con la compañía de ESA fila', async () => {
    const user = userEvent.setup();
    mocks.listPersonBiometricValidations.mockResolvedValue({
      documentType: 'CC', documentNumber: '1020445118', name: 'Carolina Pérez B',
      validations: [], page: 1, pageSize: 20, total: 0, allTerminal: true,
    });
    renderValidaciones();
    await screen.findByText('Carolina Pérez B');

    await user.click(screen.getByRole('button', { name: /acciones de validación de carolina pérez b/i }));
    await user.click(await screen.findByRole('menuitem', { name: /ver proceso/i }));

    expect(mocks.setActiveTramitesTenant).toHaveBeenCalledWith(TENANT_B);
  });

  it('AC7 — «Nueva prevalidación» en «Todas» pide primero la compañía', async () => {
    const user = userEvent.setup();
    renderValidaciones();
    await screen.findByText('Carolina Pérez B');

    await user.click(screen.getByRole('button', { name: /crear nueva prevalidación de identidad/i }));

    expect(await screen.findByRole('dialog', { name: /para qué compañía es la prevalidación/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Continuar' })).toBeDisabled();
  });
});

describe('Administrador de Compañía — mismo alcance de hoy (AC8)', () => {
  beforeEach(() => {
    mocks.listTenantBiometricPersons.mockResolvedValue(respuesta([persona({ tenantId: TENANT_A })]));
  });

  it('no ve selector ni columna Compañía y la petición no fija compañía', async () => {
    renderValidaciones();

    await screen.findByText('Carolina Pérez');
    expect(mocks.listTenantBiometricPersons).toHaveBeenCalledWith(expect.any(Object), undefined);
    expect(mocks.listStuckIdentityValidations).toHaveBeenCalledWith(undefined);
    expect(screen.queryByRole('combobox', { name: 'Compañía' })).not.toBeInTheDocument();
    expect(screen.queryByText('Compañía')).not.toBeInTheDocument();
    expect(mocks.listCompanies).not.toHaveBeenCalled();
  });

  it('AC1 — barra en una línea: buscador, Periodo, + Filtro y Columnas; sin el panel ni «Buscar»', async () => {
    renderValidaciones();
    await screen.findByText('Carolina Pérez');

    expect(screen.getByPlaceholderText('Nombre o número de documento')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /periodo/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /\+ filtro/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /columnas/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^buscar$/i })).not.toBeInTheDocument();
  });

  it('el buscador aplica al dejar de teclear y distingue documento de nombre', async () => {
    const user = userEvent.setup();
    renderValidaciones();
    await screen.findByText('Carolina Pérez');

    await user.type(screen.getByPlaceholderText('Nombre o número de documento'), '1020445118');

    await waitFor(() => expect(ultimaPeticion()[0]).toMatchObject({ documentNumber: '1020445118', page: 1 }));
    expect(ultimaPeticion()[0].name).toBeUndefined();
  });

  it('AC3 — «+ Filtro» › Origen › Prevalidación envía standalone=true, deja un chip y se quita con su ✕', async () => {
    const user = userEvent.setup();
    renderValidaciones();
    await screen.findByText('Carolina Pérez');

    await user.click(screen.getByRole('button', { name: /\+ filtro/i }));
    await user.click(screen.getByRole('button', { name: 'Origen' }));
    await user.click(screen.getByRole('radio', { name: 'Prevalidación' }));
    await user.click(screen.getByRole('button', { name: 'Agregar filtro' }));
    await user.click(screen.getByRole('button', { name: 'Aplicar' }));

    await waitFor(() => expect(ultimaPeticion()[0]).toMatchObject({ standalone: true }));
    expect(screen.getByText('Origen: Prevalidación')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Limpiar todo' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Quitar filtro Origen: Prevalidación' }));
    await waitFor(() => expect(ultimaPeticion()[0].standalone).toBeUndefined());
  });

  it('AC10 — un número de días inválido no se aplica, muestra el motivo y no pide nada', async () => {
    const user = userEvent.setup();
    renderValidaciones();
    await screen.findByText('Carolina Pérez');
    const peticiones = mocks.listTenantBiometricPersons.mock.calls.length;

    await user.click(screen.getByRole('button', { name: /\+ filtro/i }));
    await user.click(screen.getByRole('button', { name: 'Vence en ≤ N días' }));
    await user.type(screen.getByRole('textbox', { name: 'Días' }), 'abc');
    await user.click(screen.getByRole('button', { name: 'Agregar filtro' }));

    expect(screen.getByRole('alert')).toHaveTextContent(/solo números enteros/);
    expect(mocks.listTenantBiometricPersons).toHaveBeenCalledTimes(peticiones);
  });

  it('AC9 — pie de Trámites: Filas por página 10/25/50/100 y «Mostrando X–Y de N»', async () => {
    renderValidaciones();
    await screen.findByText('Carolina Pérez');

    const select = screen.getByRole('combobox', { name: 'Filas por página' });
    expect(within(select).getAllByRole('option').map((o) => o.textContent)).toEqual(['10', '25', '50', '100']);
    expect(screen.getByText('Mostrando 1–1 de 1')).toBeInTheDocument();
  });

  it('AC11 — con error de la API se conserva el aviso con reintento y los chips aplicados', async () => {
    const user = userEvent.setup();
    renderValidaciones();
    await screen.findByText('Carolina Pérez');
    mocks.listTenantBiometricPersons.mockRejectedValue(new Error('Servicio no disponible'));

    await user.click(screen.getByRole('button', { name: /\+ filtro/i }));
    await user.click(screen.getByRole('button', { name: 'Estado' }));
    await user.click(screen.getByRole('radio', { name: 'Rechazado' }));
    await user.click(screen.getByRole('button', { name: 'Agregar filtro' }));
    await user.click(screen.getByRole('button', { name: 'Aplicar' }));

    expect(await screen.findByText('No se pudieron cargar las validaciones.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reintentar' })).toBeInTheDocument();
    expect(screen.getByText('Estado: Rechazado')).toBeInTheDocument();
  });
});
