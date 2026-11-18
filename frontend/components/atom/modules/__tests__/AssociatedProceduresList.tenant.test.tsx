/**
 * Bug #14603 — el enlace a «Trámites asociados» lleva la compañía del registro (`?t=`) para el Super Admin
 * y nada para el usuario de compañía; los dos paneles de Identidad pasan el tenant del registro.
 */
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const mocks = vi.hoisted(() => ({
  superAdmin: true,
  listPersonBiometricValidations: vi.fn(),
  getPrevalidacionDetail: vi.fn(),
  getBiometricAuditByValidation: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    listPersonBiometricValidations: mocks.listPersonBiometricValidations,
    getPrevalidacionDetail: mocks.getPrevalidacionDetail,
    getBiometricAuditByValidation: mocks.getBiometricAuditByValidation,
  },
}));

vi.mock('@/lib/auth/jwt', async (orig) => ({
  ...(await orig<typeof import('@/lib/auth/jwt')>()),
  isSuperAdmin: () => mocks.superAdmin,
}));

import { AssociatedProceduresList } from '@/components/atom/modules/AssociatedProceduresList';
import { PersonIdentityDetailDrawer } from '@/components/atom/modules/PersonIdentityDetailDrawer';
import { PrevalidacionDetailDrawer } from '@/components/atom/modules/PrevalidacionDetailDrawer';
import type { BiometricValidation } from '@/lib/api/types/procedure-runtime';

const PROCS = [{ instanceId: 'inst-1', referenceNumber: 'FT1-0000001', primary: true }];

function validacion(): BiometricValidation {
  return {
    id: 'v-1',
    partyRole: null,
    name: 'Persona de prueba',
    documentType: 'CC',
    documentNumber: '1000000001',
    email: 'prueba@example.com',
    status: 'aprobado',
    intentos: 1,
    maxIntentos: 3,
    score: 90,
    expiresAt: '2026-10-30T10:00:00Z',
    validatedAt: '2026-10-05T10:00:00Z',
    expired: false,
    provider: 'kyverum',
    captureUrl: null,
    createdAt: '2026-10-05T09:00:00Z',
    procedureInstanceId: 'inst-1',
    referenceNumber: 'FT1-0000001',
    modalidad: 'traspaso',
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.superAdmin = true;
  mocks.getBiometricAuditByValidation.mockResolvedValue({ validationId: 'v-1', events: [], referencedFromOtherProcedure: false });
});

describe('AssociatedProceduresList — enlace con compañía', () => {
  it('Super Admin con tenant: href con ?t=', async () => {
    render(<AssociatedProceduresList procedures={PROCS} tenantId="ten-9" />);
    expect(await screen.findByRole('link')).toHaveAttribute('href', '/tramites/inst-1?t=ten-9');
  });

  it('usuario de compañía: href sin ?t=', () => {
    mocks.superAdmin = false;
    render(<AssociatedProceduresList procedures={PROCS} tenantId="ten-9" />);
    expect(screen.getByRole('link')).toHaveAttribute('href', '/tramites/inst-1');
  });

  it('Super Admin sin tenant del registro: href sin ?t=', () => {
    render(<AssociatedProceduresList procedures={PROCS} />);
    expect(screen.getByRole('link')).toHaveAttribute('href', '/tramites/inst-1');
  });
});

describe('Paneles de Identidad pasan el tenant del registro', () => {
  it('PersonIdentityDetailDrawer', async () => {
    mocks.listPersonBiometricValidations.mockResolvedValue({
      documentType: 'CC',
      documentNumber: '1000000001',
      name: 'Persona de prueba',
      validations: [validacion()],
      page: 1,
      pageSize: 50,
      total: 1,
      allTerminal: true,
    });
    render(<PersonIdentityDetailDrawer documentType="CC" documentNumber="1000000001" tenantId="ten-9" onClose={() => {}} />);
    await userEvent.click(await screen.findByRole('button', { name: /ver trámites asociados/i }));
    expect(await screen.findByRole('link', { name: /FT1-0000001/ })).toHaveAttribute('href', '/tramites/inst-1?t=ten-9');
  });

  it('PrevalidacionDetailDrawer', async () => {
    mocks.getPrevalidacionDetail.mockResolvedValue(validacion());
    render(<PrevalidacionDetailDrawer validationId="v-1" tenantId="ten-9" onClose={() => {}} />);
    await userEvent.click(await screen.findByRole('button', { name: /ver trámites asociados/i }));
    expect(await screen.findByRole('link', { name: /FT1-0000001/ })).toHaveAttribute('href', '/tramites/inst-1?t=ten-9');
  });
});
