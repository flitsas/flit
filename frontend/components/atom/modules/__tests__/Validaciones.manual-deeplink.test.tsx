/**
 * HU13301 — enlace profundo `?m=validaciones&tab=manuales&manual=<id>`: activa la pestaña manual y abre
 * el detalle de esa validación; solo el Super Admin; id inexistente → aviso; al cerrar se limpia `manual`.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const mocks = vi.hoisted(() => ({
  isSuperAdmin: true,
  search: '',
  listTenantBiometricPersons: vi.fn(),
  listStuckIdentityValidations: vi.fn(),
  listCompanies: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useSearchParams: () => new URLSearchParams(mocks.search),
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

const DETALLE = 'Detalle de validación manual';

function irA(search: string) {
  mocks.search = search;
  window.history.replaceState(null, '', `/?${search}`);
}

function renderValidaciones() {
  return render(
    <ToastProvider>
      <Validaciones />
    </ToastProvider>,
  );
}

afterEach(() => vi.unstubAllEnvs());

beforeEach(() => {
  vi.stubEnv('NEXT_PUBLIC_MANUAL_REVIEW_MOCK', 'true');
  vi.clearAllMocks();
  mocks.isSuperAdmin = true;
  mocks.search = '';
  window.history.replaceState(null, '', '/');
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

describe('enlace profundo a una validación manual', () => {
  it('al montar con tab y manual activa la pestaña y abre el detalle de ESA validación', async () => {
    irA('m=validaciones&tab=manuales&manual=mock-manual-01');
    renderValidaciones();
    expect(await screen.findByRole('tab', { name: /^Validaciones manuales/, selected: true })).toBeInTheDocument();
    const dialog = await screen.findByRole('dialog', { name: DETALLE });
    expect(await within(dialog).findByText("Persona de prueba 01")).toBeInTheDocument();
  });

  it('al cerrar el detalle limpia `manual` de la URL y conserva `tab`', async () => {
    irA('m=validaciones&tab=manuales&manual=mock-manual-01');
    const user = userEvent.setup();
    renderValidaciones();
    const dialog = await screen.findByRole('dialog', { name: DETALLE });
    await within(dialog).findByText("Persona de prueba 01");
    await user.keyboard('{Escape}');
    await waitFor(() => expect(screen.queryByRole('dialog', { name: DETALLE })).not.toBeInTheDocument());
    expect(window.location.search).toContain('tab=manuales');
    expect(window.location.search).toContain('m=validaciones');
    expect(window.location.search).not.toContain('manual=');
  });

  it('un id inexistente muestra un aviso claro dentro de la pestaña y no deja un modal', async () => {
    irA('m=validaciones&tab=manuales&manual=no-existe');
    renderValidaciones();
    expect(await screen.findByText('No encontramos esa validación manual.')).toBeInTheDocument();
    expect(screen.queryByRole('dialog', { name: DETALLE })).not.toBeInTheDocument();
    expect(window.location.search).not.toContain('manual=');
  });

  it('con solo `tab=manuales` abre la pestaña sin detalle', async () => {
    irA('m=validaciones&tab=manuales');
    renderValidaciones();
    expect(await screen.findByRole('tab', { name: /^Validaciones manuales/, selected: true })).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('quien no es Super Admin ignora los parámetros: lista normal, sin pestañas ni detalle', async () => {
    mocks.isSuperAdmin = false;
    irA('m=validaciones&tab=manuales&manual=mock-manual-01');
    renderValidaciones();
    await screen.findByText(/Aún no hay validaciones de identidad/);
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
