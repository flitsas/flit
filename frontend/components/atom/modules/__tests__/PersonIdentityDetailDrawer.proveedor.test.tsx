/**
 * HU-C8 (#13303) — el detalle de Identidad no nombra a Kyverum en validaciones manuales ni simuladas.
 * kyverum → «Intentos Kyverum»; mock → «Intentos»; manual → sin fila de intentos.
 */
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';

const mocks = vi.hoisted(() => ({
  listPersonBiometricValidations: vi.fn(),
  getBiometricAuditByValidation: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    listPersonBiometricValidations: mocks.listPersonBiometricValidations,
    getBiometricAuditByValidation: mocks.getBiometricAuditByValidation,
  },
}));

import { PersonIdentityDetailDrawer } from '@/components/atom/modules/PersonIdentityDetailDrawer';
import type { BiometricValidation } from '@/lib/api/types/procedure-runtime';

function validacion(overrides: Partial<BiometricValidation> = {}): BiometricValidation {
  return {
    id: 'v-1',
    partyRole: null,
    name: 'Persona de prueba',
    documentType: 'CC',
    documentNumber: '1000000001',
    email: 'prueba@example.com',
    status: 'en_proceso',
    intentos: 0,
    maxIntentos: 5,
    score: null,
    expiresAt: '2026-10-30T10:00:00Z',
    validatedAt: null,
    expired: false,
    provider: 'kyverum',
    captureUrl: 'https://captura.example.com/abc',
    createdAt: '2026-10-05T09:00:00Z',
    ...overrides,
  };
}

function renderCon(v: BiometricValidation) {
  mocks.listPersonBiometricValidations.mockResolvedValue({
    documentType: 'CC',
    documentNumber: '1000000001',
    name: 'Persona de prueba',
    validations: [v],
    page: 1,
    pageSize: 50,
    total: 1,
    allTerminal: false,
  });
  return render(<PersonIdentityDetailDrawer documentType="CC" documentNumber="1000000001" onClose={() => {}} />);
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getBiometricAuditByValidation.mockResolvedValue({ validationId: 'v-1', events: [], referencedFromOtherProcedure: false });
});

describe('Detalle de Identidad: etiqueta de intentos según proveedor', () => {
  it('kyverum conserva «Intentos Kyverum» con su contador y «Abrir captura Kyverum»', async () => {
    renderCon(validacion({ provider: 'kyverum', intentos: 2 }));
    expect(await screen.findByText('Intentos Kyverum')).toBeInTheDocument();
    expect(screen.getByText('2 / 5')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Abrir captura Kyverum' })).toBeInTheDocument();
  });

  it('mock muestra «Intentos» sin nombrar al proveedor', async () => {
    renderCon(validacion({ provider: 'mock' }));
    expect(await screen.findByText('Intentos')).toBeInTheDocument();
    expect(screen.getByText('0 / 5')).toBeInTheDocument();
    expect(screen.queryByText(/Kyverum/i)).not.toBeInTheDocument();
  });

  it('manual no muestra la fila de intentos y ninguna etiqueta visible contiene «Kyverum»', async () => {
    renderCon(validacion({ provider: 'manual', approvalOrigin: null }));
    await screen.findByText('Fecha de registro');
    expect(screen.queryByText('Intentos')).not.toBeInTheDocument();
    expect(screen.queryByText('0 / 5')).not.toBeInTheDocument();
    expect(screen.queryByText(/Kyverum/i)).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Abrir enlace de captura' })).toBeInTheDocument();
    expect(document.body.textContent ?? "").not.toMatch(/Kyverum/i);
    expect(document.body.querySelector('[aria-label*="Kyverum" i], [title*="Kyverum" i]')).toBeNull();
  });
});
