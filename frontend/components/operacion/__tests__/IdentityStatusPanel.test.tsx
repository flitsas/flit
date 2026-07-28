// HU #10875 (Feature #10863) — Vista consolidada de identidad por trámite con alertas.
// AC1: un solo panel con el estado de identidad de TODOS los actores del trámite.
// AC2: alertas/recordatorios in-app POR PULL (badges + banner), sin campana ni push.
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import type {
  BiometricValidation,
  IdentityValidationAlert,
  ProcedureActor,
} from '@/lib/api/types/procedure-runtime';

const mocks = vi.hoisted(() => ({
  // El panel resuelve la modalidad por su cuenta (autosuficiente); los tests pasan además la prop
  // `modalidad`, que tiene precedencia, así que este mock solo evita que el Promise.all falle.
  getWizardState: vi.fn(),
  getActors: vi.fn(),
  getBiometricState: vi.fn(),
  getInstanceIdentityValidationAlerts: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
}));

import { IdentityStatusPanel } from '../IdentityStatusPanel';

function actor(overrides: Partial<ProcedureActor> = {}): ProcedureActor {
  return {
    rol: 'comprador',
    tipoDocumento: 'CC',
    numeroDocumento: '1234567890',
    nombreCompleto: 'Ana Gómez',
    email: 'ana@example.com',
    ...overrides,
  };
}

function validation(overrides: Partial<BiometricValidation> = {}): BiometricValidation {
  return {
    id: 'val-comprador',
    partyRole: 'comprador',
    name: 'Ana Gómez',
    documentType: 'CC',
    documentNumber: '1234567890',
    email: 'ana@example.com',
    status: 'aprobado',
    intentos: 1,
    maxIntentos: 3,
    score: 95,
    expiresAt: '2026-08-01T00:00:00Z',
    validatedAt: '2026-07-01T00:00:00Z',
    expired: false,
    provider: 'mock',
    captureUrl: null,
    ...overrides,
  };
}

function alert(overrides: Partial<IdentityValidationAlert> = {}): IdentityValidationAlert {
  return {
    id: 'val-comprador',
    instanceId: 'inst-1',
    referenceNumber: 'REF-1',
    recipientUserId: 'user-1',
    partyRole: 'comprador',
    name: 'Ana Gómez',
    documentType: 'CC',
    documentNumber: '1234567890',
    status: 'rechazado',
    alertKind: 'rechazada',
    requiresResendReminder: false,
    daysRemainingVigencia: null,
    expiresAt: null,
    createdAt: '2026-07-01T00:00:00Z',
    ...overrides,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getWizardState.mockResolvedValue({ modalidad: 'traspaso' });
});

describe('IdentityStatusPanel — 4 estados de UI', () => {
  it('sin instanceId (trámite diferido, HU #10883): no renderiza nada', () => {
    const { container } = render(
      <IdentityStatusPanel instanceId={null} modalidad="matricula_inicial" />,
    );
    expect(container).toBeEmptyDOMElement();
    expect(mocks.getActors).not.toHaveBeenCalled();
  });

  it('estado cargando: pinta el skeleton accesible mientras llega la primera respuesta', async () => {
    let resolveActors: (v: ProcedureActor[]) => void = () => {};
    mocks.getActors.mockReturnValue(new Promise((res) => (resolveActors = res)));
    mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'mock' });
    mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({ alerts: [], total: 0 });

    render(<IdentityStatusPanel instanceId="inst-1" modalidad="matricula_inicial" />);

    expect(screen.getByText(/Cargando estado de identidad/i)).toBeInTheDocument();
    resolveActors([]);
    await waitFor(() =>
      expect(screen.queryByText(/Cargando estado de identidad/i)).not.toBeInTheDocument(),
    );
  });

  it('estado error: muestra el mensaje del fallo con role="alert" y permite reintentar', async () => {
    mocks.getActors.mockRejectedValue(new Error('Falla de red'));
    mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'mock' });
    mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({ alerts: [], total: 0 });

    render(<IdentityStatusPanel instanceId="inst-1" modalidad="matricula_inicial" />);

    const alertBox = await screen.findByRole('alert');
    expect(alertBox).toHaveTextContent('Falla de red');
    expect(screen.getByRole('button', { name: 'Reintentar' })).toBeInTheDocument();
  });

  it('estado vacío: sin actores capturados todavía, informa que no hay actores registrados', async () => {
    mocks.getActors.mockResolvedValue([]);
    mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'mock' });
    mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({ alerts: [], total: 0 });

    render(<IdentityStatusPanel instanceId="inst-1" modalidad="traspaso" />);

    expect(
      await screen.findByText(/No hay actores registrados en este trámite todavía/i),
    ).toBeInTheDocument();
  });

  it('estado lleno (AC1): un solo panel con el estado de identidad de TODOS los actores del trámite', async () => {
    mocks.getActors.mockResolvedValue([
      actor({ rol: 'comprador', nombreCompleto: 'Ana Gómez' }),
      actor({ rol: 'vendedor', nombreCompleto: 'Luis Pérez', numeroDocumento: '999' }),
    ]);
    mocks.getBiometricState.mockResolvedValue({
      validations: [
        validation({ id: 'val-comprador', partyRole: 'comprador', status: 'aprobado', name: 'Ana Gómez' }),
        validation({
          id: 'val-vendedor',
          partyRole: 'vendedor',
          status: 'en_proceso',
          name: 'Luis Pérez',
          score: null,
          validatedAt: null,
        }),
      ],
      provider: 'mock',
    });
    mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({ alerts: [], total: 0 });

    render(<IdentityStatusPanel instanceId="inst-1" modalidad="traspaso" />);

    const panel = await screen.findByRole('region', {
      name: 'Estado de identidad de los actores del trámite',
    });
    expect(within(panel).getByText('Ana Gómez')).toBeInTheDocument();
    expect(within(panel).getByText('Luis Pérez')).toBeInTheDocument();
    expect(within(panel).getByText('Aprobado')).toBeInTheDocument();
    expect(within(panel).getByText('En proceso')).toBeInTheDocument();
  });
});

describe('IdentityStatusPanel — AC2: alertas y recordatorios POR PULL', () => {
  it('mapea "rechazada" con el badge y el resumen correspondientes', async () => {
    mocks.getActors.mockResolvedValue([actor()]);
    mocks.getBiometricState.mockResolvedValue({
      validations: [validation({ id: 'val-comprador', status: 'rechazado' })],
      provider: 'mock',
    });
    mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({
      alerts: [alert({ id: 'val-comprador', alertKind: 'rechazada' })],
      total: 1,
    });

    render(<IdentityStatusPanel instanceId="inst-1" modalidad="matricula_inicial" />);

    await screen.findByText('Rechazada');
    expect(screen.getByText(/requiere atención: Comprador \(Rechazada\)/i)).toBeInTheDocument();
  });

  it('mapea "por_vencer" con tono de advertencia', async () => {
    mocks.getActors.mockResolvedValue([actor()]);
    mocks.getBiometricState.mockResolvedValue({
      validations: [validation({ id: 'val-comprador', status: 'aprobado' })],
      provider: 'mock',
    });
    mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({
      alerts: [
        alert({
          id: 'val-comprador',
          status: 'aprobado',
          alertKind: 'por_vencer',
          requiresResendReminder: true,
          daysRemainingVigencia: 5,
        }),
      ],
      total: 1,
    });

    render(<IdentityStatusPanel instanceId="inst-1" modalidad="matricula_inicial" />);

    expect(await screen.findByText('Por vencer')).toBeInTheDocument();
  });

  it('mapea "atascada" y NO ofrece recordatorio de reenvío para esa parte (mutuamente excluyentes)', async () => {
    mocks.getActors.mockResolvedValue([actor()]);
    mocks.getBiometricState.mockResolvedValue({
      validations: [validation({ id: 'val-comprador', status: 'error_envio' })],
      provider: 'mock',
    });
    mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({
      alerts: [
        alert({ id: 'val-comprador', status: 'error_envio', alertKind: 'atascada', requiresResendReminder: false }),
      ],
      total: 1,
    });

    render(<IdentityStatusPanel instanceId="inst-1" modalidad="matricula_inicial" />);

    expect(await screen.findByText('Atascada')).toBeInTheDocument();
    expect(screen.queryByText(/Recordatorio: reenviar enlace de captura/i)).not.toBeInTheDocument();
  });

  it('recordatorio de reenvío (AC2) sin alerta accionable: pinta el aviso de recordatorio', async () => {
    mocks.getActors.mockResolvedValue([actor()]);
    mocks.getBiometricState.mockResolvedValue({
      validations: [validation({ id: 'val-comprador', status: 'pendiente_envio' })],
      provider: 'mock',
    });
    mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({
      alerts: [
        alert({
          id: 'val-comprador',
          status: 'pendiente_envio',
          alertKind: null,
          requiresResendReminder: true,
        }),
      ],
      total: 1,
    });

    render(<IdentityStatusPanel instanceId="inst-1" modalidad="matricula_inicial" />);

    expect(await screen.findByText(/Recordatorio: reenviar enlace de captura/i)).toBeInTheDocument();
    expect(
      screen.getByText(/Recordatorio: reenvía el enlace de captura a Comprador/i),
    ).toBeInTheDocument();
  });

  it('sin alertas ni recordatorios: no muestra el banner de resumen', async () => {
    mocks.getActors.mockResolvedValue([actor()]);
    mocks.getBiometricState.mockResolvedValue({
      validations: [validation({ id: 'val-comprador', status: 'aprobado' })],
      provider: 'mock',
    });
    mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({ alerts: [], total: 0 });

    render(<IdentityStatusPanel instanceId="inst-1" modalidad="matricula_inicial" />);

    await screen.findByText('Aprobado');
    expect(screen.queryByText(/requiere atención/i)).not.toBeInTheDocument();
  });
});
