import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { BiometricValidation, InstanceSummary } from '@/lib/api/types/procedure-runtime';

const mocks = vi.hoisted(() => ({
  listBiometricExpediente: vi.fn(),
  downloadBiometricCertificado: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
}));

import { TramiteDetalleIdentidad } from '@/components/operacion/detalle/TramiteDetalleIdentidad';

const ITEM_TRASPASO: InstanceSummary = {
  id: 'inst-1',
  referenceNumber: 'TR-001',
  modalidad: 'traspaso',
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
  identityValidationStatus: 'aprobado',
  signaturePending: false,
  canSubmit: true,
  prioritario: false,
  tenantId: '11111111-1111-1111-1111-111111111111',
  companiaNombre: null,
};

const ITEM_MATRICULA: InstanceSummary = {
  ...ITEM_TRASPASO,
  modalidad: 'matricula_inicial',
  identityValidationStatus: null,
};

function validation(overrides: Partial<BiometricValidation>): BiometricValidation {
  return {
    id: 'val-1',
    partyRole: null,
    name: 'Titular de la validación',
    documentType: 'CC',
    documentNumber: '12345678',
    email: 'titular@example.com',
    status: 'aprobado',
    intentos: 1,
    maxIntentos: 3,
    score: 95,
    expiresAt: '2026-07-18T00:00:00Z',
    validatedAt: '2026-06-20T15:30:00Z',
    expired: false,
    provider: 'kyverum',
    captureUrl: null,
    createdAt: '2026-06-20T15:00:00Z',
    ...overrides,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe('TramiteDetalleIdentidad', () => {
  it('muestra el estado de cargando mientras llega la respuesta', () => {
    mocks.listBiometricExpediente.mockReturnValue(new Promise(() => {}));

    const { container } = render(
      <TramiteDetalleIdentidad instanceId="inst-1" item={ITEM_TRASPASO} />,
    );

    const cargando = container.querySelector(
      '[aria-label="Cargando la validación de identidad del trámite"]',
    );
    expect(cargando).toBeInTheDocument();
    expect(cargando).toHaveAttribute('aria-busy', 'true');
  });

  it('muestra error con reintento y recarga al hacer clic', async () => {
    const user = userEvent.setup();
    mocks.listBiometricExpediente.mockRejectedValueOnce(new Error('Fallo de red'));

    render(<TramiteDetalleIdentidad instanceId="inst-1" item={ITEM_TRASPASO} />);

    expect(await screen.findByRole('alert')).toHaveTextContent('Fallo de red');

    mocks.listBiometricExpediente.mockResolvedValueOnce({ validations: [], firmaBaulPartes: [] });
    // El botón lleva contexto en su nombre accesible: comparte panel con la cronología y los
    // archivos finales, y tres «Reintentar» idénticos no se distinguen por lista de botones.
    await user.click(screen.getByRole('button', { name: 'Reintentar la validación de identidad' }));

    expect(
      await screen.findByText(
        'Este trámite todavía no tiene validación de identidad iniciada para ninguna de sus partes.',
      ),
    ).toBeInTheDocument();
    expect(mocks.listBiometricExpediente).toHaveBeenCalledTimes(2);
  });

  it('vacío: un trámite sin validaciones ni acreditación por baúl no es un error', async () => {
    mocks.listBiometricExpediente.mockResolvedValue({ validations: [], firmaBaulPartes: [] });

    render(<TramiteDetalleIdentidad instanceId="inst-1" item={ITEM_MATRICULA} />);

    expect(
      await screen.findByText(
        'Este trámite todavía no tiene validación de identidad iniciada para ninguna de sus partes.',
      ),
    ).toBeInTheDocument();
  });

  it('matrícula inicial: pinta al comprador único (partyRole null) con su estado', async () => {
    mocks.listBiometricExpediente.mockResolvedValue({
      validations: [validation({ id: 'val-comprador', partyRole: null, status: 'aprobado' })],
      firmaBaulPartes: [],
    });

    render(<TramiteDetalleIdentidad instanceId="inst-1" item={ITEM_MATRICULA} />);

    const fila = (await screen.findByText('Comprador')).closest('li') as HTMLElement;
    expect(within(fila).getByText('Aprobado')).toBeInTheDocument();
    expect(within(fila).getByText('2026/06/20')).toBeInTheDocument();
  });

  it('traspaso: un renglón por parte, con estados distintos y sin mezclar datos entre partes', async () => {
    mocks.listBiometricExpediente.mockResolvedValue({
      validations: [
        validation({ id: 'val-vendedor', partyRole: 'vendedor', status: 'aprobado' }),
        validation({
          id: 'val-comprador',
          partyRole: 'comprador',
          status: 'en_proceso',
          validatedAt: null,
        }),
      ],
      firmaBaulPartes: [],
    });

    render(<TramiteDetalleIdentidad instanceId="inst-1" item={ITEM_TRASPASO} />);

    const filaVendedor = (await screen.findByText('Vendedor')).closest('li') as HTMLElement;
    const filaComprador = screen.getByText('Comprador').closest('li') as HTMLElement;

    expect(within(filaVendedor).getByText('Aprobado')).toBeInTheDocument();
    expect(within(filaComprador).getByText('En proceso')).toBeInTheDocument();
    // Sin `validatedAt` (aún no aprobada), la fecha bajo la etiqueta cae a `createdAt`: informa
    // cuándo se envió, no inventa una validación que no ha ocurrido.
    expect(within(filaComprador).getByText('2026/06/20')).toBeInTheDocument();
  });

  it('traspaso: la parte sin validación ni acreditación por baúl queda "Sin iniciar", no oculta', async () => {
    mocks.listBiometricExpediente.mockResolvedValue({
      validations: [validation({ id: 'val-vendedor', partyRole: 'vendedor', status: 'aprobado' })],
      firmaBaulPartes: [],
    });

    render(<TramiteDetalleIdentidad instanceId="inst-1" item={ITEM_TRASPASO} />);

    const filaComprador = (await screen.findByText('Comprador')).closest('li') as HTMLElement;
    expect(within(filaComprador).getByText('Sin iniciar')).toBeInTheDocument();
  });

  it('una parte acreditada por firma del baúl se rotula distinto y sin botón de certificado', async () => {
    mocks.listBiometricExpediente.mockResolvedValue({
      validations: [validation({ id: 'val-vendedor', partyRole: 'vendedor', status: 'aprobado' })],
      firmaBaulPartes: ['comprador'],
    });

    render(<TramiteDetalleIdentidad instanceId="inst-1" item={ITEM_TRASPASO} />);

    const filaComprador = (await screen.findByText('Comprador')).closest('li') as HTMLElement;
    expect(within(filaComprador).getByText('Acreditado por firma del baúl')).toBeInTheDocument();
    expect(within(filaComprador).queryByRole('button')).not.toBeInTheDocument();
  });

  it('lleno: una validación aprobada ofrece el botón de descargar certificado, que dispara la descarga', async () => {
    const user = userEvent.setup();
    mocks.listBiometricExpediente.mockResolvedValue({
      validations: [validation({ id: 'val-comprador', partyRole: null, status: 'aprobado' })],
      firmaBaulPartes: [],
    });
    const blob = new Blob(['pdf'], { type: 'application/pdf' });
    mocks.downloadBiometricCertificado.mockResolvedValue({
      blob,
      filename: 'certificado_identidad.pdf',
      mimetype: 'application/pdf',
    });
    const createObjectURL = vi.fn(() => 'blob:mock-url');
    const revokeObjectURL = vi.fn();
    vi.stubGlobal('URL', { ...URL, createObjectURL, revokeObjectURL });

    render(<TramiteDetalleIdentidad instanceId="inst-1" item={ITEM_MATRICULA} />);

    const boton = await screen.findByRole('button', { name: 'Descargar certificado de Comprador' });
    await user.click(boton);

    expect(mocks.downloadBiometricCertificado).toHaveBeenCalledWith(
      'inst-1',
      'val-comprador',
      undefined,
    );
    expect(createObjectURL).toHaveBeenCalledWith(blob);

    vi.unstubAllGlobals();
  });

  it('un estado rechazado o expirado no ofrece botón de certificado', async () => {
    mocks.listBiometricExpediente.mockResolvedValue({
      validations: [validation({ id: 'val-comprador', partyRole: null, status: 'rechazado' })],
      firmaBaulPartes: [],
    });

    render(<TramiteDetalleIdentidad instanceId="inst-1" item={ITEM_MATRICULA} />);

    const fila = (await screen.findByText('Comprador')).closest('li') as HTMLElement;
    expect(within(fila).getByText('Rechazado')).toBeInTheDocument();
    expect(within(fila).queryByRole('button')).not.toBeInTheDocument();
  });
});
