// HU #10875 (Feature #10863) + CF-08 — Historial de identidad (disclosure como Historial de estados).
// AC1: actores del trámite. AC2: alertas por pull. CF-08: solo aprobaciones/rechazos + fecha.
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type {
  BiometricValidation,
  IdentityValidationAlert,
  ProcedureActor,
} from '@/lib/api/types/procedure-runtime';

const mocks = vi.hoisted(() => ({
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

async function openHistory(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: /historial de identidad/i }));
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getWizardState.mockResolvedValue({ modalidad: 'traspaso' });
});

describe('IdentityStatusPanel — disclosure (patrón Historial de estados)', () => {
  it('sin instanceId: no renderiza nada', () => {
    const { container } = render(
      <IdentityStatusPanel instanceId={null} modalidad="matricula_inicial" />,
    );
    expect(container).toBeEmptyDOMElement();
    expect(mocks.getActors).not.toHaveBeenCalled();
  });

  it('colapsado por defecto: no consulta el backend hasta abrir', () => {
    render(<IdentityStatusPanel instanceId="inst-1" modalidad="matricula_inicial" />);
    expect(screen.getByRole('button', { name: /▸ historial de identidad/i })).toBeInTheDocument();
    expect(mocks.getActors).not.toHaveBeenCalled();
  });

  it('al abrir carga y muestra skeleton mientras llega la respuesta', async () => {
    const user = userEvent.setup();
    let resolveActors: (v: ProcedureActor[]) => void = () => {};
    mocks.getActors.mockReturnValue(new Promise((res) => (resolveActors = res)));
    mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'mock' });
    mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({ alerts: [], total: 0 });

    render(<IdentityStatusPanel instanceId="inst-1" modalidad="matricula_inicial" />);
    await openHistory(user);

    expect(screen.getByText(/Cargando historial de identidad/i)).toBeInTheDocument();
    resolveActors([]);
    await waitFor(() =>
      expect(screen.queryByText(/Cargando historial de identidad/i)).not.toBeInTheDocument(),
    );
  });

  it('estado error: muestra el mensaje con role="alert" y permite reintentar', async () => {
    const user = userEvent.setup();
    mocks.getActors.mockRejectedValue(new Error('Falla de red'));
    mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'mock' });
    mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({ alerts: [], total: 0 });

    render(<IdentityStatusPanel instanceId="inst-1" modalidad="matricula_inicial" />);
    await openHistory(user);

    const alertBox = await screen.findByRole('alert');
    expect(alertBox).toHaveTextContent('Falla de red');
    expect(screen.getByRole('button', { name: 'Reintentar' })).toBeInTheDocument();
  });

  it('estado vacío: sin actores, informa que no hay actores registrados', async () => {
    const user = userEvent.setup();
    mocks.getActors.mockResolvedValue([]);
    mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'mock' });
    mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({ alerts: [], total: 0 });

    render(<IdentityStatusPanel instanceId="inst-1" modalidad="traspaso" />);
    await openHistory(user);

    expect(
      await screen.findByText(/No hay actores registrados en este trámite todavía/i),
    ).toBeInTheDocument();
  });

  it('estado lleno (AC1): lista procesos de todos los actores con fecha', async () => {
    const user = userEvent.setup();
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
          status: 'rechazado',
          name: 'Luis Pérez',
          score: null,
          validatedAt: '2026-07-02T00:00:00Z',
        }),
      ],
      provider: 'mock',
    });
    mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({ alerts: [], total: 0 });

    render(<IdentityStatusPanel instanceId="inst-1" modalidad="traspaso" />);
    await openHistory(user);

    const panel = await screen.findByRole('region', {
      name: 'Historial de identidad del trámite',
    });
    expect(within(panel).getByText('Ana Gómez')).toBeInTheDocument();
    expect(within(panel).getByText('Luis Pérez')).toBeInTheDocument();
    expect(within(panel).getByText('Aprobado')).toBeInTheDocument();
    expect(within(panel).getByText('Rechazado')).toBeInTheDocument();
  });
});

describe('IdentityStatusPanel — CF-08: solo aprobaciones y rechazos', () => {
  it('omite procesos en curso del listado y muestra el rechazo con fecha', async () => {
    const user = userEvent.setup();
    mocks.getActors.mockResolvedValue([actor()]);
    mocks.getBiometricState.mockResolvedValue({
      validations: [
        validation({ id: 'val-1', status: 'rechazado', validatedAt: null }),
        validation({ id: 'val-2', status: 'en_proceso', validatedAt: null, score: null }),
      ],
      provider: 'kyverum',
    });
    mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({ alerts: [], total: 0 });

    render(<IdentityStatusPanel instanceId="inst-1" modalidad="matricula_inicial" />);
    await openHistory(user);

    expect(await screen.findByText('Rechazado')).toBeInTheDocument();
    expect(screen.queryByText('En proceso')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /actualizar/i })).not.toBeInTheDocument();
  });

  it('con una aprobación la muestra vigente y con fecha', async () => {
    const user = userEvent.setup();
    mocks.getActors.mockResolvedValue([actor()]);
    mocks.getBiometricState.mockResolvedValue({
      validations: [validation({ id: 'val-1', status: 'aprobado', provider: 'kyverum' })],
      provider: 'kyverum',
    });
    mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({ alerts: [], total: 0 });

    render(<IdentityStatusPanel instanceId="inst-1" modalidad="matricula_inicial" />);
    await openHistory(user);

    expect(await screen.findByText('Aprobado')).toBeInTheDocument();
    expect(screen.getByText('Vigente')).toBeInTheDocument();
  });
});

describe('IdentityStatusPanel — AC2: alertas y recordatorios POR PULL', () => {
  it('mapea "rechazada" con el badge y el resumen correspondientes', async () => {
    const user = userEvent.setup();
    mocks.getActors.mockResolvedValue([actor()]);
    mocks.getBiometricState.mockResolvedValue({
      validations: [validation({ id: 'val-comprador', status: 'rechazado' })],
      provider: 'mock',
    });
    mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({
      alerts: [alert({ alertKind: 'rechazada' })],
      total: 1,
    });

    render(<IdentityStatusPanel instanceId="inst-1" modalidad="matricula_inicial" />);
    await openHistory(user);

    expect(await screen.findByText(/requiere atención/i)).toBeInTheDocument();
    expect(screen.getByText(/Comprador \(Rechazada\)/i)).toBeInTheDocument();
  });

  it('mapea "expirada"', async () => {
    const user = userEvent.setup();
    mocks.getActors.mockResolvedValue([actor()]);
    mocks.getBiometricState.mockResolvedValue({
      validations: [validation({ id: 'val-comprador', status: 'expirado', expired: true })],
      provider: 'mock',
    });
    mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({
      alerts: [alert({ alertKind: 'expirada', status: 'expirado' })],
      total: 1,
    });

    render(<IdentityStatusPanel instanceId="inst-1" modalidad="matricula_inicial" />);
    await openHistory(user);

    expect(await screen.findByText(/Comprador \(Expirada\)/i)).toBeInTheDocument();
  });

  it('mapea "por_vencer" y "atascada"', async () => {
    const user = userEvent.setup();
    mocks.getActors.mockResolvedValue([
      actor({ rol: 'comprador' }),
      actor({ rol: 'vendedor', nombreCompleto: 'Luis', numeroDocumento: '2' }),
    ]);
    mocks.getBiometricState.mockResolvedValue({
      validations: [
        validation({ id: 'val-c', partyRole: 'comprador', status: 'aprobado' }),
        validation({ id: 'val-v', partyRole: 'vendedor', status: 'en_proceso', name: 'Luis' }),
      ],
      provider: 'mock',
    });
    mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({
      alerts: [
        alert({ id: 'val-c', alertKind: 'por_vencer', partyRole: 'comprador', status: 'aprobado' }),
        alert({ id: 'val-v', alertKind: 'atascada', partyRole: 'vendedor', name: 'Luis', status: 'en_proceso' }),
      ],
      total: 2,
    });

    render(<IdentityStatusPanel instanceId="inst-1" modalidad="traspaso" />);
    await openHistory(user);

    expect(await screen.findByText(/Comprador \(Por vencer\)/i)).toBeInTheDocument();
    expect(screen.getByText(/Vendedor \(Atascada\)/i)).toBeInTheDocument();
  });

  it('muestra recordatorio de reenvío cuando applies', async () => {
    const user = userEvent.setup();
    mocks.getActors.mockResolvedValue([actor()]);
    mocks.getBiometricState.mockResolvedValue({
      validations: [validation({ id: 'val-comprador', status: 'enviado', validatedAt: null })],
      provider: 'mock',
    });
    mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({
      alerts: [
        alert({
          alertKind: null,
          requiresResendReminder: true,
          status: 'enviado',
        }),
      ],
      total: 1,
    });

    render(<IdentityStatusPanel instanceId="inst-1" modalidad="matricula_inicial" />);
    await openHistory(user);

    expect(await screen.findByText(/Recordatorio: reenvía el enlace de captura/i)).toBeInTheDocument();
  });

  it('sin alertas ni recordatorios: no muestra el banner de resumen', async () => {
    const user = userEvent.setup();
    mocks.getActors.mockResolvedValue([actor()]);
    mocks.getBiometricState.mockResolvedValue({
      validations: [validation({ id: 'val-comprador', status: 'aprobado' })],
      provider: 'mock',
    });
    mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({ alerts: [], total: 0 });

    render(<IdentityStatusPanel instanceId="inst-1" modalidad="matricula_inicial" />);
    await openHistory(user);

    await screen.findByText('Aprobado');
    expect(screen.queryByText(/requiere atención/i)).not.toBeInTheDocument();
  });
});
