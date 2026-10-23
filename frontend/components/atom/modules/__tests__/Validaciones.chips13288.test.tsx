/**
 * HU #13288 — los chips de ESTADO de la tabla de Identidad no se desbordan de su columna: la tabla usa la
 * etiqueta CORTA de los estados manuales largos, el chip lleva la red de seguridad de clases (se parte en
 * líneas en vez de montarse sobre la celda vecina) y el texto completo vive en `title` / `aria-label`.
 */
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';

const mocks = vi.hoisted(() => ({
  listTenantBiometricPersons: vi.fn(),
  listStuckIdentityValidations: vi.fn(),
}));

vi.mock('@/hooks/useNetworkScope', () => ({
  useNetworkScope: () => ({
    isGroupParent: false,
    scope: { mode: 'own' },
    networkActive: false,
    setScope: vi.fn(),
    children: [],
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
    listStuckIdentityValidations: mocks.listStuckIdentityValidations,
  },
  setActiveTramitesTenant: vi.fn(),
  TramitesApiError: class TramitesApiError extends Error {},
  getIdentitySendConflict: () => null,
}));

import { ToastProvider } from '@/components/admin/Toast';
import { Validaciones } from '@/components/atom/modules/Validaciones';
import { MANUAL_ESTADO_META } from '@/lib/identity/manual-flow';
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

const PERSONAS = [
  persona({ name: 'Ana Espera', latestValidationId: 'v1', status: 'manual_activo', provider: 'manual', maxIntentos: 5 }),
  persona({ name: 'Beto Revision', latestValidationId: 'v2', status: 'pendiente_revision_manual', provider: 'manual', maxIntentos: 5 }),
  persona({ name: 'Cata Enviada', latestValidationId: 'v3', status: 'enviado' }),
  persona({ name: 'Dani Rechazada', latestValidationId: 'v4', status: 'rechazado', intentos: 1, maxIntentos: 5 }),
  persona({ name: 'Eli Pendiente', latestValidationId: 'v5', status: 'pendiente_envio' }),
];

beforeEach(() => {
  vi.clearAllMocks();
  mocks.listStuckIdentityValidations.mockResolvedValue({ stuck: [], total: 0, maxDeliveryAttempts: 5 });
  mocks.listTenantBiometricPersons.mockResolvedValue({
    persons: PERSONAS,
    stats: { total: 5, aprobadas: 0, enProceso: 5, rechazadas: 0, expiradas: 0 },
    page: 1,
    pageSize: 10,
    total: 5,
  });
});

async function filaDe(nombre: string): Promise<HTMLElement> {
  render(
    <ToastProvider>
      <Validaciones />
    </ToastProvider>,
  );
  const celda = await screen.findByText(nombre);
  return celda.closest('tr') as HTMLElement;
}

describe('Chips de estado de la tabla de Identidad (HU #13288)', () => {
  it('«Esperando captura del cliente» se muestra corto en la tabla y completo en title y aria-label', async () => {
    const fila = await filaDe('Ana Espera');
    const chip = within(fila).getByRole('status', { name: 'Estado: Esperando captura del cliente' });
    expect(chip).toHaveTextContent(/^Esperando captura$/);
    expect(chip).toHaveAttribute('title', 'Esperando captura del cliente');
    expect(fila.getAttribute('aria-label')).toMatch(/estado Esperando captura del cliente/);
  });

  it('el chip de la tabla lleva la red de seguridad: max-w-full, min-w-0, texto que se parte y centrado', async () => {
    const fila = await filaDe('Ana Espera');
    const chip = within(fila).getByRole('status', { name: /Estado: / });
    for (const cls of ['max-w-full', 'min-w-0', 'whitespace-normal', 'text-center', 'leading-tight']) {
      expect(chip.className).toContain(cls);
    }
    expect(chip.className).not.toContain('whitespace-nowrap');
  });

  it('«Pendiente de revisión» ya es corta: mismo texto en tabla y title', async () => {
    const fila = await filaDe('Beto Revision');
    const chip = within(fila).getByRole('status', { name: 'Estado: Pendiente de revisión' });
    expect(chip).toHaveTextContent('Pendiente de revisión');
    expect(chip).toHaveAttribute('title', 'Pendiente de revisión');
  });

  it('los demás estados no cambian de texto y también llevan title con su texto completo', async () => {
    render(
      <ToastProvider>
        <Validaciones />
      </ToastProvider>,
    );
    const enviada = (await screen.findByText('Cata Enviada')).closest('tr') as HTMLElement;
    expect(within(enviada).getByRole('status', { name: 'Estado: Enviado' })).toHaveAttribute('title', 'Enviado');
    const pendiente = (await screen.findByText('Eli Pendiente')).closest('tr') as HTMLElement;
    expect(within(pendiente).getByText('Pendiente de envío')).toHaveAttribute('title', 'Pendiente de envío');
    const rechazada = (await screen.findByText('Dani Rechazada')).closest('tr') as HTMLElement;
    expect(within(rechazada).getByText('Rechazado (intentos disponibles)')).toHaveAttribute(
      'title',
      'Rechazado (intentos disponibles)',
    );
  });

  it('MANUAL_ESTADO_META conserva la etiqueta larga del detalle y suma la corta de la tabla', () => {
    expect(MANUAL_ESTADO_META.manual_activo.label).toBe('Esperando captura del cliente');
    expect(MANUAL_ESTADO_META.manual_activo.shortLabel).toBe('Esperando captura');
    expect(MANUAL_ESTADO_META.pendiente_revision_manual.label).toBe('Pendiente de revisión');
    expect(MANUAL_ESTADO_META.pendiente_revision_manual.shortLabel).toBe('Pendiente de revisión');
  });
});
