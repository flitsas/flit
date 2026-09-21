/**
 * HU #12709 — Validación de Identidad con el alcance de red de la cabeza (Concesión / Marca Blanca):
 * «Mi compañía» igual que hoy; «Toda la red» o una hija por las rutas `network/**`; filas de hijas en
 * solo consulta; crear e incidencias solo en lo propio; red no disponible ⇒ vuelve a «Mi compañía».
 */
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const CABEZA = 'c0000000-0000-4000-8000-000000000100';
const HIJA_A = 'c0000000-0000-4000-8000-000000001100';

const mocks = vi.hoisted(() => ({
  red: {
    isGroupParent: true,
    scope: { mode: 'own' } as { mode: 'own' | 'network'; childTenantId?: string },
    networkActive: false,
  },
  setScope: vi.fn(),
  listTenantBiometricPersons: vi.fn(),
  listNetworkIdentityPersons: vi.fn(),
  listNetworkPersonIdentityValidations: vi.fn(),
  listPersonBiometricValidations: vi.fn(),
  listStuckIdentityValidations: vi.fn(),
  setActiveTramitesTenant: vi.fn(),
}));

vi.mock('@/hooks/useNetworkScope', () => ({
  useNetworkScope: () => ({
    ...mocks.red,
    setScope: mocks.setScope,
    children: [{ id: 'c0000000-0000-4000-8000-000000001100', nombre: 'Hija Andina SAS' }],
    childrenStatus: 'ready',
    ready: true,
    saving: false,
  }),
}));
vi.mock('@/lib/api/superadmin-client', () => ({ superadminClient: { listCompanies: vi.fn() } }));
vi.mock('@/lib/api/client', () => ({ getToken: () => 'token' }));
vi.mock('@/lib/auth/jwt', () => ({
  decodeJwtPayload: () => ({ tenant_id: 'c0000000-0000-4000-8000-000000000100' }),
  isSuperAdmin: () => false,
  hasPermission: () => false,
}));
vi.mock('@/lib/api/ui-preferences', () => ({
  uiPreferencesClient: {
    get: vi.fn().mockResolvedValue({ scope: 'identidad.columns', value: {} }),
    put: vi.fn().mockResolvedValue({ scope: 'identidad.columns', value: {} }),
  },
}));
vi.mock('@/lib/api/tramites-client', () => ({
  ALL_TENANTS: '*',
  tramitesClient: {
    listTenantBiometricPersons: mocks.listTenantBiometricPersons,
    listNetworkIdentityPersons: mocks.listNetworkIdentityPersons,
    listNetworkPersonIdentityValidations: mocks.listNetworkPersonIdentityValidations,
    listPersonBiometricValidations: mocks.listPersonBiometricValidations,
    listStuckIdentityValidations: mocks.listStuckIdentityValidations,
    getNetworkIdentityAudit: vi.fn().mockResolvedValue({ validationId: 'x', events: [] }),
    getBiometricAuditByValidation: vi.fn().mockResolvedValue({ validationId: 'x', events: [] }),
  },
  setActiveTramitesTenant: mocks.setActiveTramitesTenant,
  TramitesApiError: class TramitesApiError extends Error {},
  getIdentitySendConflict: () => null,
}));

import { ToastProvider } from '@/components/admin/Toast';
import { Validaciones } from '@/components/atom/modules/Validaciones';
import type { TenantBiometricPerson } from '@/lib/api/types/procedure-runtime';

function persona(overrides: Partial<TenantBiometricPerson>): TenantBiometricPerson {
  return {
    documentType: 'CC',
    documentNumber: '1020445118',
    name: 'Persona',
    status: 'enviado',
    validationCount: 1,
    worstAlertKind: null,
    latestValidationId: 'val',
    instanceId: null,
    referenceNumber: null,
    modalidad: null,
    partyRole: null,
    email: 'persona@correo.co',
    provider: 'kyverum',
    score: null,
    captureUrl: null,
    expired: false,
    intentos: 0,
    maxIntentos: 3,
    createdAt: '2026-09-10T10:00:00Z',
    validatedAt: null,
    validUntil: null,
    daysRemaining: null,
    linkExpiresAt: null,
    ...overrides,
  } as TenantBiometricPerson;
}

const RED = {
  persons: [
    persona({ tenantId: CABEZA, tenantName: 'Cabeza Marca SAS', name: 'Propia Cabeza', latestValidationId: 'val-cabeza' }),
    persona({ tenantId: HIJA_A, tenantName: 'Hija Andina SAS', name: 'Cliente Hija', latestValidationId: 'val-hija', email: 'cl***@correo.co' }),
  ],
  stats: { total: 2, aprobadas: 0, enProceso: 2, rechazadas: 0, expiradas: 0 },
  page: 1,
  pageSize: 10,
  total: 2,
};

function renderValidaciones() {
  return render(
    <ToastProvider>
      <Validaciones />
    </ToastProvider>,
  );
}

function enRed(childTenantId?: string) {
  mocks.red.scope = childTenantId ? { mode: 'network', childTenantId } : { mode: 'network' };
  mocks.red.networkActive = true;
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.red.isGroupParent = true;
  mocks.red.scope = { mode: 'own' };
  mocks.red.networkActive = false;
  mocks.listStuckIdentityValidations.mockResolvedValue({ stuck: [], total: 0, maxDeliveryAttempts: 5 });
  mocks.listTenantBiometricPersons.mockResolvedValue({ ...RED, persons: [RED.persons[0]], total: 1 });
  mocks.listNetworkIdentityPersons.mockResolvedValue(RED);
});

describe('Validación de Identidad — alcance de red de la cabeza (HU #12709)', () => {
  it('AC1 — en «Mi compañía» ve el selector «Alcance» y las peticiones son las de hoy', async () => {
    renderValidaciones();

    await screen.findByText('Propia Cabeza');
    expect(screen.getByTestId('identidad-network-scope-select')).toBeInTheDocument();
    expect(mocks.listTenantBiometricPersons).toHaveBeenCalledWith(expect.any(Object), undefined);
    expect(mocks.listNetworkIdentityPersons).not.toHaveBeenCalled();
    expect(screen.queryByText('Compañía')).not.toBeInTheDocument();
  });

  it('AC2 — «Toda la red» lista por la ruta de red con la columna Compañía', async () => {
    enRed();
    renderValidaciones();

    await screen.findByText('Cliente Hija');
    expect(mocks.listNetworkIdentityPersons).toHaveBeenCalledWith(expect.objectContaining({ page: 1 }), undefined);
    expect(mocks.listTenantBiometricPersons).not.toHaveBeenCalled();
    const filas = screen.getByRole('list', { name: /validaciones de identidad/i });
    expect(within(filas).getByText('Hija Andina SAS')).toBeInTheDocument();
    expect(screen.getByTestId('identidad-network-scope-badge')).toBeInTheDocument();
  });

  it('AC2 — una hija elegida acota la ruta de red a esa compañía', async () => {
    enRed(HIJA_A);
    renderValidaciones();

    await screen.findByText('Cliente Hija');
    expect(mocks.listNetworkIdentityPersons).toHaveBeenCalledWith(expect.any(Object), HIJA_A);
  });

  it('AC3 — la fila de la hija es «Solo consulta» y solo ofrece «Ver proceso»; la propia conserva sus acciones', async () => {
    const user = userEvent.setup();
    enRed();
    renderValidaciones();
    await screen.findByText('Cliente Hija');

    const filaHija = screen.getByRole('listitem', { name: /Cliente Hija/ });
    expect(within(filaHija).getByText('Solo consulta')).toBeInTheDocument();
    await user.click(within(filaHija).getByRole('button', { name: /acciones de validación de cliente hija/i }));
    const acciones = await screen.findAllByRole('menuitem');
    expect(acciones.map((a) => a.textContent)).toEqual(['Ver proceso']);
    await user.keyboard('{Escape}');

    const filaPropia = screen.getByRole('listitem', { name: /Propia Cabeza/ });
    expect(within(filaPropia).queryByText('Solo consulta')).not.toBeInTheDocument();
    await user.click(within(filaPropia).getByRole('button', { name: /acciones de validación de propia cabeza/i }));
    expect((await screen.findAllByRole('menuitem')).map((a) => a.textContent)).toContain('Editar');
  });

  it('AC3 — el detalle de una persona de la hija se lee por la ruta de red, en solo consulta', async () => {
    const user = userEvent.setup();
    mocks.listNetworkPersonIdentityValidations.mockResolvedValue({
      documentType: 'CC', documentNumber: '1020445118', name: 'Cliente Hija',
      validations: [], page: 1, pageSize: 50, total: 0, allTerminal: true,
    });
    enRed();
    renderValidaciones();
    await screen.findByText('Cliente Hija');

    await user.click(screen.getByRole('button', { name: /acciones de validación de cliente hija/i }));
    await user.click(await screen.findByRole('menuitem', { name: /ver proceso/i }));

    await waitFor(() =>
      expect(mocks.listNetworkPersonIdentityValidations).toHaveBeenCalledWith(HIJA_A, 'CC', '1020445118', expect.any(Object)),
    );
    expect(mocks.listPersonBiometricValidations).not.toHaveBeenCalled();
    expect(within(screen.getByRole('dialog')).getByText('Solo consulta')).toBeInTheDocument();
  });

  it('AC4 — en la red «Nueva prevalidación» queda deshabilitada y las incidencias son solo las propias', async () => {
    enRed();
    renderValidaciones();
    await screen.findByText('Cliente Hija');

    const nueva = screen.getByRole('button', { name: /crear nueva prevalidación de identidad/i });
    expect(nueva).toBeDisabled();
    expect(nueva).toHaveTextContent('Disponible en Mi compañía');
    expect(mocks.listStuckIdentityValidations).toHaveBeenCalledWith(undefined);
  });

  it('AC7 — si la red responde 403, vuelve a «Mi compañía» sin mostrar un fallo con reintento', async () => {
    enRed();
    mocks.listNetworkIdentityPersons.mockRejectedValue(Object.assign(new Error('network_scope_required'), { status: 403 }));
    renderValidaciones();

    await screen.findByText('Propia Cabeza');
    expect(mocks.listTenantBiometricPersons).toHaveBeenCalled();
    expect(screen.queryByText('No se pudieron cargar las validaciones.')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reintentar' })).not.toBeInTheDocument();
  });
});

describe('Quién no ve el selector (AC6)', () => {
  it('un usuario que no es administrador de una cabeza no ve «Alcance» ni pide la red', async () => {
    mocks.red.isGroupParent = false;
    renderValidaciones();

    await screen.findByText('Propia Cabeza');
    expect(screen.queryByTestId('identidad-network-scope-select')).not.toBeInTheDocument();
    expect(mocks.listNetworkIdentityPersons).not.toHaveBeenCalled();
  });
});
