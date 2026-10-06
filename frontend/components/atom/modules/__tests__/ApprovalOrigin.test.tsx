/**
 * HU-C8 (#13303) — «Origen de la aprobación» en el detalle de una validación de Identidad:
 * chip «Automática» o «Manual»; sin origen no se muestra la fila.
 */
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';

const mocks = vi.hoisted(() => ({
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

import { PersonIdentityDetailDrawer } from '@/components/atom/modules/PersonIdentityDetailDrawer';
import { PrevalidacionDetailDrawer } from '@/components/atom/modules/PrevalidacionDetailDrawer';
import { ApprovalOriginChip } from '@/components/atom/modules/ApprovalOriginChip';
import type { BiometricValidation } from '@/lib/api/types/procedure-runtime';

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
    maxIntentos: 3,
    score: 90,
    expiresAt: '2026-10-30T10:00:00Z',
    validatedAt: '2026-10-05T10:00:00Z',
    expired: false,
    provider: 'kyverum',
    captureUrl: null,
    createdAt: '2026-10-05T09:00:00Z',
    ...overrides,
  };
}

function personaCon(v: BiometricValidation) {
  mocks.listPersonBiometricValidations.mockResolvedValue({
    documentType: 'CC',
    documentNumber: '1000000001',
    name: 'Persona de prueba',
    validations: [v],
    page: 1,
    pageSize: 50,
    total: 1,
    allTerminal: true,
  });
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getBiometricAuditByValidation.mockResolvedValue({ validationId: 'v-1', events: [], referencedFromOtherProcedure: false });
});

describe('Detalle de Identidad por persona', () => {
  it('aprobación automática: fila «Origen de la aprobación» con «Automática»', async () => {
    personaCon(validacion({ approvalOrigin: 'automatica' }));
    render(<PersonIdentityDetailDrawer documentType="CC" documentNumber="1000000001" onClose={() => {}} />);
    expect(await screen.findByText('Origen de la aprobación')).toBeInTheDocument();
    expect(screen.getByRole('status', { name: 'Origen de la aprobación: Automática' })).toBeInTheDocument();
  });

  it('aprobación manual: chip «Manual» junto a la fecha de aprobación, sin nombre de revisor', async () => {
    personaCon(validacion({ approvalOrigin: 'manual' }));
    render(<PersonIdentityDetailDrawer documentType="CC" documentNumber="1000000001" onClose={() => {}} />);
    const chip = await screen.findByRole('status', { name: 'Origen de la aprobación: Manual' });
    expect(chip).toHaveTextContent('Manual');
    expect(chip.querySelector('svg')).not.toBeNull();
    expect(screen.getByText('Fecha aprobación')).toBeInTheDocument();
    expect(screen.queryByText(/revis(or|ó)/i)).not.toBeInTheDocument();
  });

  it.each([[null], [undefined]])('sin origen (%s) la fila no se muestra', async (origin) => {
    personaCon(validacion({ status: 'en_proceso', validatedAt: null, approvalOrigin: origin }));
    render(<PersonIdentityDetailDrawer documentType="CC" documentNumber="1000000001" onClose={() => {}} />);
    await screen.findByText('Fecha aprobación');
    expect(screen.queryByText('Origen de la aprobación')).not.toBeInTheDocument();
  });
});

describe('Detalle de prevalidación', () => {
  it('muestra el origen cuando existe y lo omite cuando es null', async () => {
    mocks.getPrevalidacionDetail.mockResolvedValueOnce(validacion({ approvalOrigin: 'manual' }));
    const { unmount } = render(<PrevalidacionDetailDrawer validationId="v-1" onClose={() => {}} />);
    expect(await screen.findByText('Origen de la aprobación')).toBeInTheDocument();
    unmount();

    mocks.getPrevalidacionDetail.mockResolvedValueOnce(validacion({ approvalOrigin: null }));
    render(<PrevalidacionDetailDrawer validationId="v-1" onClose={() => {}} />);
    await screen.findByText('Intentos Kyverum');
    expect(screen.queryByText('Origen de la aprobación')).not.toBeInTheDocument();
  });
});

describe('Motivo de rechazo en el detalle de Identidad', () => {
  const GENERICO = 'No se pudo verificar la identidad con las imágenes recibidas.';

  it('manual rechazada con código: muestra la etiqueta elegida por el Super Admin, no el texto genérico', async () => {
    personaCon(validacion({
      status: 'rechazado', provider: 'manual', approvalOrigin: null,
      rejectionReason: GENERICO, rejectionReasonCode: 'imagen_borrosa',
    }));
    render(<PersonIdentityDetailDrawer documentType="CC" documentNumber="1000000001" onClose={() => {}} />);
    expect(await screen.findByText('Motivo del rechazo: Imagen borrosa')).toBeInTheDocument();
    expect(screen.queryByText(new RegExp(GENERICO))).not.toBeInTheDocument();
  });

  it('manual rechazada sin código (o con uno desconocido): conserva el texto de siempre', async () => {
    personaCon(validacion({
      status: 'rechazado', provider: 'manual', approvalOrigin: null,
      rejectionReason: GENERICO, rejectionReasonCode: 'codigo_que_no_existe',
    }));
    render(<PersonIdentityDetailDrawer documentType="CC" documentNumber="1000000001" onClose={() => {}} />);
    expect(await screen.findByText(`Motivo del rechazo: ${GENERICO}`)).toBeInTheDocument();
  });

  it.each(['kyverum', 'mock'])('%s rechazada: no cambia su texto aunque llegara un código', async (provider) => {
    personaCon(validacion({
      status: 'rechazado', provider, approvalOrigin: null,
      rejectionReason: 'Rostro no coincide', rejectionReasonCode: 'imagen_borrosa',
    }));
    render(<PersonIdentityDetailDrawer documentType="CC" documentNumber="1000000001" onClose={() => {}} />);
    expect(await screen.findByText('Motivo del rechazo: Rostro no coincide')).toBeInTheDocument();
  });
});

describe('ApprovalOriginChip', () => {
  it.each([
    ['automatica', 'Automática'],
    ['manual', 'Manual'],
  ] as const)('%s → «%s»', (origin, texto) => {
    render(<ApprovalOriginChip origin={origin} />);
    expect(screen.getByRole('status')).toHaveTextContent(texto);
  });
});
