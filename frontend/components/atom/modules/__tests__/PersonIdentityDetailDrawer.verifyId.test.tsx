/**
 * Ajustes 2026-11-10 (punto 4) — el detalle de Identidad muestra el id de la verificación en Kyverum Verify
 * (`kyverumVerificationId`) con un botón de copiar accesible. Sin id → «—» y sin botón.
 */
// Uso de ejemplo: <PersonIdentityDetailDrawer documentType="CC" documentNumber="1000000001" onClose={...} />
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const mocks = vi.hoisted(() => ({
  listPersonBiometricValidations: vi.fn(),
  listNetworkPersonIdentityValidations: vi.fn(),
  getBiometricAuditByValidation: vi.fn(),
  getNetworkBiometricAuditByValidation: vi.fn(),
  getNetworkIdentityAudit: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
}));

import { PersonIdentityDetailDrawer } from '@/components/atom/modules/PersonIdentityDetailDrawer';
import type { BiometricValidation } from '@/lib/api/types/procedure-runtime';

const VERIFY_ID = 'kv-0f3a9c2e-1b7d-4e55-9a10-2c4b6d8e0f12';

function validacion(overrides: Partial<BiometricValidation> = {}): BiometricValidation {
  return {
    id: 'v-1',
    partyRole: null,
    name: 'Persona de prueba',
    documentType: 'CC',
    documentNumber: '1000000001',
    email: 'prueba@example.com',
    status: 'aprobado',
    intentos: 1,
    maxIntentos: 5,
    score: 98,
    expiresAt: '2026-10-30T10:00:00Z',
    validatedAt: '2026-10-05T09:10:00Z',
    expired: false,
    provider: 'kyverum',
    captureUrl: null,
    createdAt: '2026-10-05T09:00:00Z',
    ...overrides,
  };
}

function respuesta(v: BiometricValidation) {
  return {
    documentType: 'CC',
    documentNumber: '1000000001',
    name: 'Persona de prueba',
    validations: [v],
    page: 1,
    pageSize: 50,
    total: 1,
    allTerminal: true,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getBiometricAuditByValidation.mockResolvedValue({ validationId: 'v-1', events: [], referencedFromOtherProcedure: false });
  mocks.getNetworkBiometricAuditByValidation.mockResolvedValue({ validationId: 'v-1', events: [], referencedFromOtherProcedure: false });
  mocks.getNetworkIdentityAudit.mockResolvedValue({ validationId: 'v-1', events: [], referencedFromOtherProcedure: false });
});

describe('Detalle de Identidad: ID de Verify', () => {
  it('muestra el id de Verify en fuente monoespaciada y el botón de copiar', async () => {
    mocks.listPersonBiometricValidations.mockResolvedValue(respuesta(validacion({ kyverumVerificationId: VERIFY_ID })));
    render(<PersonIdentityDetailDrawer documentType="CC" documentNumber="1000000001" onClose={() => {}} />);
    expect(await screen.findByText('ID de Verify')).toBeInTheDocument();
    const valor = screen.getByText(VERIFY_ID);
    expect(valor.className).toContain('font-mono');
    expect(valor.className).toContain('select-all');
    expect(screen.getByRole('button', { name: 'Copiar ID de Verify' })).toBeInTheDocument();
  });

  it('sin id (null) muestra «—» y no ofrece el botón de copiar', async () => {
    mocks.listPersonBiometricValidations.mockResolvedValue(respuesta(validacion({ kyverumVerificationId: null })));
    render(<PersonIdentityDetailDrawer documentType="CC" documentNumber="1000000001" onClose={() => {}} />);
    const etiqueta = await screen.findByText('ID de Verify');
    expect(etiqueta.parentElement?.textContent).toContain('—');
    expect(screen.queryByRole('button', { name: 'Copiar ID de Verify' })).not.toBeInTheDocument();
  });

  it('sin el campo (respuesta anterior) también muestra «—» sin botón', async () => {
    mocks.listPersonBiometricValidations.mockResolvedValue(respuesta(validacion()));
    render(<PersonIdentityDetailDrawer documentType="CC" documentNumber="1000000001" onClose={() => {}} />);
    const etiqueta = await screen.findByText('ID de Verify');
    expect(etiqueta.parentElement?.textContent).toContain('—');
    expect(screen.queryByRole('button', { name: 'Copiar ID de Verify' })).not.toBeInTheDocument();
  });

  it('copiar llama clipboard.writeText con el id y anuncia «Copiado» en una región aria-live', async () => {
    const user = userEvent.setup();
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    mocks.listPersonBiometricValidations.mockResolvedValue(respuesta(validacion({ kyverumVerificationId: VERIFY_ID })));
    render(<PersonIdentityDetailDrawer documentType="CC" documentNumber="1000000001" onClose={() => {}} />);
    await user.click(await screen.findByRole('button', { name: 'Copiar ID de Verify' }));
    expect(writeText).toHaveBeenCalledWith(VERIFY_ID);
    await waitFor(() => {
      const region = screen.getByText('Copiado');
      expect(region.closest('[aria-live="polite"]')).not.toBeNull();
    });
  });

  it('en solo consulta (red) también se ve el id con su botón de copiar', async () => {
    mocks.listNetworkPersonIdentityValidations.mockResolvedValue(respuesta(validacion({ kyverumVerificationId: VERIFY_ID })));
    render(
      <PersonIdentityDetailDrawer
        documentType="CC"
        documentNumber="1000000001"
        networkTenantId="tenant-hija"
        onClose={() => {}}
      />,
    );
    expect(await screen.findByText(VERIFY_ID)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Copiar ID de Verify' })).toBeInTheDocument();
  });
});
