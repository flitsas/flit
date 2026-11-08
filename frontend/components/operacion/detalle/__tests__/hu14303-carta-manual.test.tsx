/**
 * HU #14303 (Feature #14300) — carta de la validación manual aprobada: descarga o «en preparación».
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { BiometricValidation, InstanceSummary } from '@/lib/api/types/procedure-runtime';
import { estadoCertificado } from '@/lib/identity/certificado-validacion';

const mocks = vi.hoisted(() => ({
  listBiometricExpediente: vi.fn(),
  downloadBiometricCertificado: vi.fn(),
  downloadValidationCertificado: vi.fn(),
  getPrevalidacionDetail: vi.fn(),
  listPersonBiometricValidations: vi.fn(),
  getBiometricAuditByValidation: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
}));

import { TramiteDetalleIdentidad } from '@/components/operacion/detalle/TramiteDetalleIdentidad';
import { PrevalidacionDetailDrawer } from '@/components/atom/modules/PrevalidacionDetailDrawer';
import { ManualReviewDetailModal } from '@/components/atom/modules/ManualReviewDetailModal';
import { createMockManualReviewClient } from '@/lib/api/manual-review-mock';
import type { ManualReviewClient } from '@/lib/api/manual-review-client';
import { PersonIdentityDetailDrawer } from '@/components/atom/modules/PersonIdentityDetailDrawer';

const ITEM: InstanceSummary = {
  id: 'inst-1',
  referenceNumber: 'TR-001',
  modalidad: 'MATRICULAS',
  estado: 'entregado',
  placa: 'ABC123',
  vin: 'VIN-XYZ-001',
  vehiculoMarca: 'Toyota',
  vehiculoLinea: 'Corolla',
  compradorNombre: 'Carlos Mendoza',
  compradorDocumento: '12345678',
  organismoTransito: 'Secretaría de Movilidad Bogotá',
  pasoActual: 6,
  totalPasos: 6,
  createdAt: '2026-06-18T00:00:00Z',
  draftFinalizedAt: null,
  identityValidationStatus: null,
  signaturePending: false,
  canSubmit: true,
  prioritario: false,
  tenantId: '11111111-1111-1111-1111-111111111111',
  companiaNombre: null,
};

function validation(overrides: Partial<BiometricValidation> = {}): BiometricValidation {
  return {
    id: 'val-1',
    partyRole: null,
    name: 'Titular',
    documentType: 'CC',
    documentNumber: '12345678',
    email: 'titular@example.com',
    status: 'aprobado',
    intentos: 0,
    maxIntentos: 3,
    score: null,
    expiresAt: '2026-07-18T00:00:00Z',
    validatedAt: '2026-06-20T15:30:00Z',
    expired: false,
    provider: 'manual',
    captureUrl: null,
    createdAt: '2026-06-20T15:00:00Z',
    procedureInstanceId: 'inst-1',
    ...overrides,
  };
}

function conExpediente(v: BiometricValidation) {
  mocks.listBiometricExpediente.mockResolvedValue({ validations: [v], firmaBaulPartes: [] });
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getBiometricAuditByValidation.mockResolvedValue({ validationId: 'val-1', events: [], referencedFromOtherProcedure: false });
});

describe('estadoCertificado', () => {
  it('manual aprobada: con carta es descargable; sin carta, en preparación', () => {
    expect(estadoCertificado(validation({ cartaDisponible: true }))).toBe('descargable');
    expect(estadoCertificado(validation({ cartaDisponible: false }))).toBe('en_preparacion');
    expect(estadoCertificado(validation({ cartaDisponible: undefined }))).toBe('en_preparacion');
  });
  it('Kyverum aprobada no cambia y las no aprobadas no ofrecen nada', () => {
    expect(estadoCertificado(validation({ provider: 'kyverum' }))).toBe('descargable');
    expect(estadoCertificado(validation({ status: 'rechazado', cartaDisponible: true }))).toBe('ninguno');
    expect(estadoCertificado(null)).toBe('ninguno');
  });
});

describe('HU #14303 — detalle del trámite', () => {
  it('AC1: manual aprobada con carta ofrece el botón y obtiene el PDF', async () => {
    const user = userEvent.setup();
    conExpediente(validation({ cartaDisponible: true }));
    mocks.downloadBiometricCertificado.mockResolvedValue({
      blob: new Blob(['pdf']),
      filename: 'carta.pdf',
      mimetype: 'application/pdf',
    });
    const createObjectURL = vi.fn(() => 'blob:x');
    URL.createObjectURL = createObjectURL;
    URL.revokeObjectURL = vi.fn();

    render(<TramiteDetalleIdentidad instanceId="inst-1" item={ITEM} />);
    await user.click(await screen.findByRole('button', { name: 'Descargar certificado de Comprador' }));

    expect(mocks.downloadBiometricCertificado).toHaveBeenCalledWith('inst-1', 'val-1', undefined);
    expect(createObjectURL).toHaveBeenCalled();
    expect(screen.queryByText('Certificado en preparación')).not.toBeInTheDocument();
  });

  it('AC2: manual aprobada sin carta muestra «en preparación», sin botón ni error', async () => {
    conExpediente(validation({ cartaDisponible: false }));
    render(<TramiteDetalleIdentidad instanceId="inst-1" item={ITEM} />);

    const aviso = await screen.findByText('Certificado en preparación');
    expect(aviso).toHaveAttribute('aria-live', 'polite');
    expect(screen.queryByRole('button', { name: /Descargar certificado/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('Kyverum aprobada sigue ofreciendo el botón sin depender de cartaDisponible', async () => {
    conExpediente(validation({ provider: 'kyverum' }));
    render(<TramiteDetalleIdentidad instanceId="inst-1" item={ITEM} />);
    expect(await screen.findByRole('button', { name: 'Descargar certificado de Comprador' })).toBeInTheDocument();
    expect(screen.queryByText('Certificado en preparación')).not.toBeInTheDocument();
  });

  it('si la descarga falla muestra el mensaje claro del backend', async () => {
    const user = userEvent.setup();
    conExpediente(validation({ cartaDisponible: true }));
    mocks.downloadBiometricCertificado.mockRejectedValue(new Error('No hay certificado disponible.'));
    render(<TramiteDetalleIdentidad instanceId="inst-1" item={ITEM} />);
    await user.click(await screen.findByRole('button', { name: 'Descargar certificado de Comprador' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('No hay certificado disponible.');
  });
});

describe('HU #14303 — detalle de identidad de la persona', () => {
  function abrir(v: BiometricValidation) {
    mocks.listPersonBiometricValidations.mockResolvedValue({
      documentType: 'CC',
      documentNumber: '12345678',
      name: 'Titular',
      validations: [v],
      page: 1,
      pageSize: 50,
      total: 1,
      allTerminal: true,
    });
    return render(<PersonIdentityDetailDrawer documentType="CC" documentNumber="12345678" onClose={() => {}} />);
  }

  it('AC1: manual aprobada con carta y trámite ofrece «Descargar certificado»', async () => {
    const user = userEvent.setup();
    mocks.downloadBiometricCertificado.mockResolvedValue({ blob: new Blob(['pdf']), filename: 'c.pdf', mimetype: 'application/pdf' });
    URL.createObjectURL = vi.fn(() => 'blob:x');
    URL.revokeObjectURL = vi.fn();
    abrir(validation({ cartaDisponible: true }));
    await user.click(await screen.findByRole('button', { name: 'Descargar certificado' }));
    expect(mocks.downloadBiometricCertificado).toHaveBeenCalledWith('inst-1', 'val-1');
  });

  it('AC2: manual aprobada sin carta muestra «en preparación» sin botón', async () => {
    abrir(validation({ cartaDisponible: false }));
    const panel = await screen.findByText('Certificado en preparación');
    expect(within(panel.parentElement as HTMLElement).queryByRole('button', { name: /Descargar/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Descargar certificado' })).not.toBeInTheDocument();
  });

  it('sin trámite asociado: con carta usa la ruta por validación', async () => {
    const user = userEvent.setup();
    mocks.downloadValidationCertificado.mockResolvedValue({ blob: new Blob(['pdf']), filename: 'c.pdf', mimetype: 'application/pdf' });
    URL.createObjectURL = vi.fn(() => 'blob:x');
    URL.revokeObjectURL = vi.fn();
    abrir(validation({ cartaDisponible: true, procedureInstanceId: null }));
    await user.click(await screen.findByRole('button', { name: 'Descargar certificado' }));
    expect(mocks.downloadValidationCertificado).toHaveBeenCalledWith('val-1');
    expect(mocks.downloadBiometricCertificado).not.toHaveBeenCalled();
  });

  it('sin trámite asociado: un 503 muestra el mensaje claro del backend', async () => {
    const user = userEvent.setup();
    mocks.downloadValidationCertificado.mockRejectedValue(new Error('La carta aún no está disponible.'));
    abrir(validation({ cartaDisponible: true, procedureInstanceId: null }));
    await user.click(await screen.findByRole('button', { name: 'Descargar certificado' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('La carta aún no está disponible.');
  });

  it('sin trámite asociado y sin carta: «en preparación»', async () => {
    abrir(validation({ cartaDisponible: false, procedureInstanceId: null }));
    expect(await screen.findByText('Certificado en preparación')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Descargar certificado' })).not.toBeInTheDocument();
  });
});

describe('HU #14303 — detalle de prevalidación y detalle manual (sin trámite)', () => {
  it('prevalidación: manual aprobada sin trámite con carta descarga por la ruta por validación', async () => {
    const user = userEvent.setup();
    mocks.getPrevalidacionDetail.mockResolvedValue(validation({ cartaDisponible: true, procedureInstanceId: null }));
    mocks.downloadValidationCertificado.mockResolvedValue({ blob: new Blob(['pdf']), filename: 'c.pdf', mimetype: 'application/pdf' });
    URL.createObjectURL = vi.fn(() => 'blob:x');
    URL.revokeObjectURL = vi.fn();
    render(<PrevalidacionDetailDrawer validationId="val-1" onClose={() => {}} />);
    await user.click(await screen.findByRole('button', { name: 'Descargar certificado' }));
    expect(mocks.downloadValidationCertificado).toHaveBeenCalledWith('val-1');
  });

  it('prevalidación: sin carta muestra «en preparación» y un caso no aprobado no muestra nada', async () => {
    mocks.getPrevalidacionDetail.mockResolvedValue(validation({ cartaDisponible: false, procedureInstanceId: null }));
    const { unmount } = render(<PrevalidacionDetailDrawer validationId="val-1" onClose={() => {}} />);
    expect(await screen.findByText('Certificado en preparación')).toBeInTheDocument();
    unmount();
    mocks.getPrevalidacionDetail.mockResolvedValue(validation({ status: 'rechazado', procedureInstanceId: null }));
    render(<PrevalidacionDetailDrawer validationId="val-1" onClose={() => {}} />);
    await screen.findByText('Rechazado');
    expect(screen.queryByText('Certificado en preparación')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Descargar certificado' })).not.toBeInTheDocument();
  });

  async function abrirModal(over: Record<string, unknown>) {
    const base = createMockManualReviewClient({ delayMs: 0, now: new Date('2026-10-05T15:00:00Z') });
    const det = await base.getManualDetail('mock-manual-01');
    const client = { ...base, getManualDetail: async () => ({ ...det, ...over }) } as ManualReviewClient;
    render(<ManualReviewDetailModal id="mock-manual-01" client={client} onClose={() => {}} />);
    await screen.findByText('Consentimiento de tratamiento de datos');
  }

  it('modal manual aprobado con carta: descarga por la ruta por validación; 503 muestra el mensaje', async () => {
    const user = userEvent.setup();
    mocks.downloadValidationCertificado.mockRejectedValue(new Error('La carta aún no está disponible.'));
    await abrirModal({ status: 'aprobado', cartaDisponible: true });
    await user.click(screen.getByRole('button', { name: 'Descargar certificado' }));
    expect(mocks.downloadValidationCertificado).toHaveBeenCalledWith('mock-manual-01');
    expect(await screen.findByRole('alert')).toHaveTextContent('La carta aún no está disponible.');
  });

  it('modal manual aprobado sin carta: «en preparación»; no aprobado: nada', async () => {
    await abrirModal({ status: 'aprobado', cartaDisponible: false });
    expect(screen.getByText('Certificado en preparación')).toBeInTheDocument();
  });

  it('modal manual pendiente de revisión no muestra nada del certificado', async () => {
    await abrirModal({ status: 'pendiente_revision_manual', cartaDisponible: false });
    expect(screen.queryByText('Certificado en preparación')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Descargar certificado' })).not.toBeInTheDocument();
  });
});
