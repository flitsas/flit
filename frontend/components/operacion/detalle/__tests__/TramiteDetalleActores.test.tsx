import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { InstanceSummary, ProcedureActor } from '@/lib/api/types/procedure-runtime';

const mocks = vi.hoisted(() => ({
  getActors: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
}));

import { TramiteDetalleActores } from '@/components/operacion/detalle/TramiteDetalleActores';

const BASE_ITEM: InstanceSummary = {
  id: 'inst-1',
  referenceNumber: 'TR-001',
  modalidad: 'TRASPASO',
  estado: 'entregado',
  placa: 'ABC123',
  vin: 'VIN-XYZ-001',
  vehiculoMarca: 'Toyota',
  vehiculoLinea: 'Corolla',
  compradorNombre: 'Comprador de prueba',
  compradorDocumento: '99999999',
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
  firmaVendedorEstado: 'firmado',
  firmaCompradorEstado: 'pendiente',
};

const VENDEDOR: ProcedureActor = {
  rol: 'vendedor',
  tipoDocumento: 'CC',
  numeroDocumento: '1017229443',
  nombreCompleto: 'Actor Vendedor Prueba',
  email: 'vendedor@example.com',
  telefono: '3009998888',
  direccion: 'Simulada 1',
};

const COMPRADOR: ProcedureActor = {
  rol: 'comprador',
  tipoDocumento: 'CC',
  numeroDocumento: '1128442907',
  nombreCompleto: 'Actor Comprador Prueba',
  email: 'comprador@example.com',
};

const COMPRADOR_JURIDICO: ProcedureActor = {
  rol: 'comprador',
  tipoDocumento: 'NIT',
  numeroDocumento: '9012345671',
  nombreCompleto: 'Compañía Simulada SAS',
  email: 'contacto@simulada.example',
  personType: 'juridical',
  representanteLegal: {
    tipoDocumento: 'CC',
    numeroDocumento: '71234980',
    nombreCompleto: 'Representante Simulado',
    mecanismoFirma: 'baul',
  },
};

// Múltiple Propietario (ADR-0053) — dos compradores del mismo lado, cada uno con su ordinal y
// porcentaje. `COMPRADOR_2` llega PRIMERO en el array a propósito: la pantalla debe ordenar por
// `ordinal`, no confiar en el orden de la respuesta.
const COMPRADOR_MP_1: ProcedureActor = {
  rol: 'comprador',
  tipoDocumento: 'CC',
  numeroDocumento: '111',
  nombreCompleto: 'Copropietario Uno',
  email: 'copro1@example.com',
  ordinal: 1,
  porcentaje: 60,
};

const COMPRADOR_MP_2: ProcedureActor = {
  rol: 'comprador',
  tipoDocumento: 'NIT',
  numeroDocumento: '9019998887',
  nombreCompleto: 'Copropietario Dos SAS',
  email: 'copro2@example.com',
  personType: 'juridical',
  ordinal: 2,
  porcentaje: 40,
  representanteLegal: {
    tipoDocumento: 'CC',
    numeroDocumento: '80112233',
    nombreCompleto: 'RL Copropietario Dos',
    mecanismoFirma: 'identidad',
  },
};

beforeEach(() => {
  vi.clearAllMocks();
});

describe('TramiteDetalleActores', () => {
  it('muestra el estado de cargando mientras llega la respuesta', () => {
    mocks.getActors.mockReturnValue(new Promise(() => {}));

    const { container } = render(<TramiteDetalleActores instanceId="inst-1" item={BASE_ITEM} />);

    const cargando = container.querySelector('[aria-label="Cargando actores del trámite"]');
    expect(cargando).toBeInTheDocument();
    expect(cargando).toHaveAttribute('aria-busy', 'true');
  });

  it('muestra error con reintento y recarga al hacer clic', async () => {
    const user = userEvent.setup();
    mocks.getActors.mockRejectedValueOnce(new Error('Fallo de red'));

    render(<TramiteDetalleActores instanceId="inst-1" item={BASE_ITEM} />);

    expect(await screen.findByRole('alert')).toHaveTextContent('Fallo de red');

    mocks.getActors.mockResolvedValueOnce([VENDEDOR, COMPRADOR]);
    await user.click(screen.getByRole('button', { name: 'Reintentar' }));

    expect(await screen.findByRole('region', { name: 'Comprador' })).toBeInTheDocument();
    expect(mocks.getActors).toHaveBeenCalledTimes(2);
  });

  it('muestra el estado vacío cuando no hay actores registrados', async () => {
    mocks.getActors.mockResolvedValue([]);

    render(<TramiteDetalleActores instanceId="inst-1" item={BASE_ITEM} />);

    expect(
      await screen.findByText('Este trámite no tiene actores registrados.'),
    ).toBeInTheDocument();
  });

  it('traspaso con dos partes: pinta vendedor y comprador con sus datos y el estado de firma de cada uno', async () => {
    mocks.getActors.mockResolvedValue([VENDEDOR, COMPRADOR]);

    render(<TramiteDetalleActores instanceId="inst-1" item={BASE_ITEM} />);

    const vendedorCard = await screen.findByRole('region', { name: 'Propietario / vendedor' });
    expect(within(vendedorCard).getByText('Actor Vendedor Prueba')).toBeInTheDocument();
    expect(within(vendedorCard).getByText('CC 1017229443')).toBeInTheDocument();
    expect(within(vendedorCard).getByText('vendedor@example.com')).toBeInTheDocument();
    expect(within(vendedorCard).getByText('3009998888')).toBeInTheDocument();
    expect(within(vendedorCard).getByText('Simulada 1')).toBeInTheDocument();
    expect(within(vendedorCard).getByText('Firmado')).toBeInTheDocument();

    const compradorCard = screen.getByRole('region', { name: 'Comprador' });
    expect(within(compradorCard).getByText('Actor Comprador Prueba')).toBeInTheDocument();
    expect(within(compradorCard).getByText('CC 1128442907')).toBeInTheDocument();
    expect(within(compradorCard).getByText('Sin firma')).toBeInTheDocument();

    // Traspaso: la propuesta no dibuja representante legal ahí, así que tampoco se pinta.
    expect(screen.queryByRole('region', { name: 'Representante legal' })).not.toBeInTheDocument();
  });

  it('matrícula inicial con una sola parte: NO aparece tarjeta de vendedor y sí la del representante legal', async () => {
    mocks.getActors.mockResolvedValue([COMPRADOR_JURIDICO]);

    render(
      <TramiteDetalleActores
        instanceId="inst-2"
        item={{ ...BASE_ITEM, modalidad: 'MATRICULAS', firmaVendedorEstado: null }}
      />,
    );

    expect(await screen.findByRole('region', { name: 'Comprador' })).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Propietario / vendedor' })).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Vendedor' })).not.toBeInTheDocument();

    const rlCard = screen.getByRole('region', { name: 'Representante legal' });
    expect(within(rlCard).getByText('Representante Simulado')).toBeInTheDocument();
    expect(within(rlCard).getByText('CC 71234980')).toBeInTheDocument();
    expect(within(rlCard).getByText('Firma del baúl')).toBeInTheDocument();
  });

  it('el estado de firma por parte muestra "Sin registrar" cuando la parte existe pero no tiene acreditación', async () => {
    mocks.getActors.mockResolvedValue([COMPRADOR]);

    render(
      <TramiteDetalleActores
        instanceId="inst-3"
        item={{
          ...BASE_ITEM,
          modalidad: 'MATRICULAS',
          firmaVendedorEstado: null,
          firmaCompradorEstado: null,
        }}
      />,
    );

    const compradorCard = await screen.findByRole('region', { name: 'Comprador' });
    expect(within(compradorCard).getByText('Sin registrar')).toBeInTheDocument();
  });

  describe('Múltiple Propietario (ADR-0053)', () => {
    it('con un solo actor por lado, el render es idéntico al de siempre (cero regresión: sin ordinal, sin % )', async () => {
      mocks.getActors.mockResolvedValue([VENDEDOR, COMPRADOR]);

      render(<TramiteDetalleActores instanceId="inst-1" item={BASE_ITEM} />);

      expect(await screen.findByRole('region', { name: 'Propietario / vendedor' })).toBeInTheDocument();
      expect(screen.getByRole('region', { name: 'Comprador' })).toBeInTheDocument();
      expect(screen.queryByRole('region', { name: /Comprador 1/ })).not.toBeInTheDocument();
      expect(screen.queryByText(/%/)).not.toBeInTheDocument();
    });

    it('lista a los 2 copropietarios ordenados por ordinal, cada uno con su porcentaje', async () => {
      // Respuesta deliberadamente desordenada (ordinal 2 primero).
      mocks.getActors.mockResolvedValue([COMPRADOR_MP_2, COMPRADOR_MP_1]);

      render(
        <TramiteDetalleActores
          instanceId="inst-mp-1"
          item={{ ...BASE_ITEM, modalidad: 'MATRICULAS', firmaVendedorEstado: null }}
        />,
      );

      const comprador1Card = await screen.findByRole('region', { name: 'Comprador 1' });
      expect(within(comprador1Card).getByText('Copropietario Uno')).toBeInTheDocument();
      expect(within(comprador1Card).getByText('60%')).toBeInTheDocument();
      // El estado de firma (dato agregado por lado) se pinta en el ordinal=1…
      expect(within(comprador1Card).getByText('Sin firma')).toBeInTheDocument();

      const comprador2Card = screen.getByRole('region', { name: 'Comprador 2' });
      expect(within(comprador2Card).getByText('Copropietario Dos SAS')).toBeInTheDocument();
      expect(within(comprador2Card).getByText('40%')).toBeInTheDocument();
      // …y NO se repite en los agregados (evita el "miente sobre el estado" al duplicar el dato).
      expect(within(comprador2Card).queryByText('Sin firma')).not.toBeInTheDocument();
      expect(within(comprador2Card).queryByText('Sin registrar')).not.toBeInTheDocument();

      // Cada copropietario jurídico tiene su propia tarjeta de representante legal, distinguible.
      expect(
        screen.getByRole('region', { name: 'Representante legal · Comprador 2' }),
      ).toBeInTheDocument();
    });

    it('no muestra tarjeta de vendedor cuando el lado no tiene actores (matrícula inicial)', async () => {
      mocks.getActors.mockResolvedValue([COMPRADOR_MP_1, COMPRADOR_MP_2]);

      render(
        <TramiteDetalleActores
          instanceId="inst-mp-2"
          item={{ ...BASE_ITEM, modalidad: 'MATRICULAS', firmaVendedorEstado: null }}
        />,
      );

      await screen.findByRole('region', { name: 'Comprador 1' });
      expect(screen.queryByRole('region', { name: /vendedor/i })).not.toBeInTheDocument();
    });
  });
});
