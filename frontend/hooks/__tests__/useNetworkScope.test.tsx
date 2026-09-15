import { renderHook, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useNetworkScope } from '../useNetworkScope';
import { usePermissions } from '@/hooks/usePermissions';
import { uiPreferencesClient } from '@/lib/api/ui-preferences';
import { fetchNetworkChildren, TramitesApiError } from '@/lib/api/tramites-client';

/**
 * HU #12556 — `useNetworkScope` consume el endpoint no-admin de hijas
 * (`GET /api/v1/tramites/network/children`, HU #12555) en vez de la ruta admin.
 *
 * Uso de ejemplo: `useNetworkScope()` en un componente de una cabeza de grupo. `children` trae la
 * lista de hijos (id + nombre) tal como la devuelve el endpoint no-admin; `childrenStatus` distingue
 * `ready` de `unavailable` (5xx/red) sin exponer nunca error al usuario; un 403
 * (`network_scope_required`) apaga `isGroupParent`, que es lo que decide si el selector se pinta.
 */

vi.mock('@/hooks/usePermissions', () => ({ usePermissions: vi.fn() }));
vi.mock('@/lib/api/ui-preferences', () => ({
  uiPreferencesClient: { get: vi.fn(), put: vi.fn() },
}));
vi.mock('@/lib/api/tramites-client', () => ({
  fetchNetworkChildren: vi.fn(),
  TramitesApiError: class TramitesApiError extends Error {
    status: number;
    problem: Record<string, unknown> | null;
    constructor(status: number, message: string, problem: Record<string, unknown> | null = null) {
      super(message);
      this.name = 'TramitesApiError';
      this.status = status;
      this.problem = problem;
    }
  },
}));

const TENANT_ID = '11111111-1111-1111-1111-111111111111';
const HIJO_A = '22222222-2222-2222-2222-222222222222';
const HIJO_B = '33333333-3333-3333-3333-333333333333';

function permisos(over: Partial<ReturnType<typeof usePermissions>> = {}) {
  return {
    permissions: [],
    isSuperAdmin: false,
    isAdminCompany: true,
    isOtAdmin: false,
    isGroupParent: false,
    hasParentTenant: false,
    parentTenantId: null,
    tenantId: TENANT_ID,
    userId: 'user-1',
    roleId: 'r1',
    roleCode: 'AdminCompany',
    ...over,
  };
}

describe('useNetworkScope', () => {
  afterEach(() => {
    vi.resetAllMocks();
  });

  it('un usuario que NO es cabeza de grupo no llama a fetchNetworkChildren (AC1)', async () => {
    vi.mocked(usePermissions).mockReturnValue(permisos({ isGroupParent: false }));
    const { result } = renderHook(() => useNetworkScope());
    expect(result.current.isGroupParent).toBe(false);
    expect(result.current.ready).toBe(true);
    expect(fetchNetworkChildren).not.toHaveBeenCalled();
  });

  it('cabeza de grupo: lista poblada — usa fetchNetworkChildren y ordena por nombre (contrato del endpoint no-admin)', async () => {
    vi.mocked(usePermissions).mockReturnValue(permisos({ isGroupParent: true }));
    vi.mocked(uiPreferencesClient.get).mockResolvedValue({ scope: 'tramites.scope', value: {} });
    vi.mocked(fetchNetworkChildren).mockResolvedValue([
      { id: HIJO_B, nombre: 'Zeta SAS' },
      { id: HIJO_A, nombre: 'Alfa SAS' },
    ]);
    const { result } = renderHook(() => useNetworkScope());

    await waitFor(() => expect(result.current.childrenStatus).toBe('ready'));
    expect(fetchNetworkChildren).toHaveBeenCalledTimes(1);
    expect(result.current.isGroupParent).toBe(true);
    expect(result.current.children).toEqual([
      { id: HIJO_A, nombre: 'Alfa SAS' },
      { id: HIJO_B, nombre: 'Zeta SAS' },
    ]);
  });

  it('403 (`network_scope_required`) — ya NO degrada la lista: apaga isGroupParent y el selector no debe pintarse (AC2)', async () => {
    vi.mocked(usePermissions).mockReturnValue(permisos({ isGroupParent: true }));
    vi.mocked(uiPreferencesClient.get).mockResolvedValue({ scope: 'tramites.scope', value: {} });
    vi.mocked(fetchNetworkChildren).mockRejectedValue(
      new TramitesApiError(403, '403 Forbidden', { error: 'network_scope_required' }),
    );
    const { result } = renderHook(() => useNetworkScope());

    await waitFor(() => expect(fetchNetworkChildren).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(result.current.isGroupParent).toBe(false));
    expect(result.current.children).toEqual([]);
    expect(result.current.childrenStatus).not.toBe('unavailable');
    // Con isGroupParent en false el alcance efectivo vuelve a ser el propio, nunca la red.
    expect(result.current.scope).toEqual({ mode: 'own' });
    expect(result.current.networkActive).toBe(false);
  });

  it('5xx / error de red — degrada a `unavailable`, mantiene isGroupParent y el selector sigue disponible sin lista (AC2)', async () => {
    vi.mocked(usePermissions).mockReturnValue(permisos({ isGroupParent: true }));
    vi.mocked(uiPreferencesClient.get).mockResolvedValue({ scope: 'tramites.scope', value: {} });
    vi.mocked(fetchNetworkChildren).mockRejectedValue(
      new TramitesApiError(500, '500 Internal Server Error', null),
    );
    const { result } = renderHook(() => useNetworkScope());

    await waitFor(() => expect(result.current.childrenStatus).toBe('unavailable'));
    expect(result.current.children).toEqual([]);
    // El selector sigue existiendo: la cabeza NO se desmiente, solo falló pedir la lista.
    expect(result.current.isGroupParent).toBe(true);
  });

  it('usuario sin grupo (SuperAdmin o sin tenant) — el selector queda oculto de raíz, sin llamada', async () => {
    vi.mocked(usePermissions).mockReturnValue(
      permisos({ isGroupParent: true, isSuperAdmin: true }),
    );
    const { result } = renderHook(() => useNetworkScope());
    expect(result.current.isGroupParent).toBe(false);
    expect(result.current.ready).toBe(true);
    expect(fetchNetworkChildren).not.toHaveBeenCalled();
  });

  it('error de red genérico (no TramitesApiError) también degrada a `unavailable`, nunca a 403', async () => {
    vi.mocked(usePermissions).mockReturnValue(permisos({ isGroupParent: true }));
    vi.mocked(uiPreferencesClient.get).mockResolvedValue({ scope: 'tramites.scope', value: {} });
    vi.mocked(fetchNetworkChildren).mockRejectedValue(new TypeError('Failed to fetch'));
    const { result } = renderHook(() => useNetworkScope());

    await waitFor(() => expect(result.current.childrenStatus).toBe('unavailable'));
    expect(result.current.isGroupParent).toBe(true);
  });
});
