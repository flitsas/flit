/**
 * HU-C5 (#13300) — la pestaña «Validaciones manuales» solo existe para el Super Admin y la lista actual no cambia.
 */
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const mocks = vi.hoisted(() => ({
  isSuperAdmin: false,
  listTenantBiometricPersons: vi.fn(),
  listStuckIdentityValidations: vi.fn(),
  listCompanies: vi.fn(),
}));

vi.mock('@/hooks/useNetworkScope', () => ({
  useNetworkScope: () => ({
    isGroupParent: false,
    scope: { mode: 'own' },
    setScope: () => {},
    networkActive: false,
    children: [],
    childrenStatus: 'idle',
    ready: true,
    saving: false,
  }),
}));
vi.mock('@/lib/api/superadmin-client', () => ({ superadminClient: { listCompanies: mocks.listCompanies } }));
vi.mock('@/lib/api/client', () => ({
  getToken: () => 'token',
  resolveApiUrl: (p: string) => p,
  apiFetch: vi.fn(),
  friendlyErrorMessage: () => '',
}));
vi.mock('@/lib/auth/jwt', () => ({
  decodeJwtPayload: () => ({}),
  isSuperAdmin: () => mocks.isSuperAdmin,
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
    listStuckIdentityValidations: mocks.listStuckIdentityValidations,
    listPersonBiometricValidations: vi.fn(),
  },
  setActiveTramitesTenant: vi.fn(),
  TramitesApiError: class TramitesApiError extends Error {},
  getIdentitySendConflict: () => null,
}));

import { ToastProvider } from '@/components/admin/Toast';
import { Validaciones } from '@/components/atom/modules/Validaciones';

function renderValidaciones() {
  return render(
    <ToastProvider>
      <Validaciones />
    </ToastProvider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.isSuperAdmin = false;
  mocks.listStuckIdentityValidations.mockResolvedValue({ stuck: [], total: 0, maxDeliveryAttempts: 5 });
  mocks.listCompanies.mockResolvedValue({ data: [] });
  mocks.listTenantBiometricPersons.mockResolvedValue({
    persons: [],
    stats: { total: 0, aprobadas: 0, enProceso: 0, rechazadas: 0, expiradas: 0 },
    page: 1,
    pageSize: 10,
    total: 0,
  });
});

describe('Pestaña «Validaciones manuales» por rol', () => {
  it('Super Admin: ve «Validaciones» y «Validaciones manuales» con el contador de pendientes; la lista actual sigue siendo la de siempre', async () => {
    mocks.isSuperAdmin = true;
    renderValidaciones();
    expect(await screen.findByRole('tablist', { name: 'Secciones de validaciones' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Validaciones', selected: true })).toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: 'Manuales' })).not.toBeInTheDocument();
    // Contador: cantidad de registros en pendiente_revision_manual (10 de las 27 filas simuladas).
    expect(await screen.findByRole('tab', { name: /^Validaciones manuales\s*· 10$/ })).toBeInTheDocument();
    await screen.findByText(/Aún no hay validaciones de identidad/);
    expect(mocks.listTenantBiometricPersons).toHaveBeenCalled();
  });

  it('Super Admin: «Validaciones manuales» muestra la tabla simulada', async () => {
    mocks.isSuperAdmin = true;
    const user = userEvent.setup();
    renderValidaciones();
    await user.click(await screen.findByRole('tab', { name: /^Validaciones manuales/ }));
    expect(await screen.findByRole('table', { name: 'Validaciones manuales' })).toBeVisible();
  });

  it('otro rol: no hay pestañas ni datos manuales', async () => {
    renderValidaciones();
    await screen.findByText(/Aún no hay validaciones de identidad/);
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: /manuales/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('table', { name: 'Validaciones manuales' })).not.toBeInTheDocument();
  });
});
