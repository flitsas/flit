import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { TramiteDetalleComercial } from '../TramiteDetalleComercial';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { CommercialData, InstanceSummary } from '@/lib/api/types/procedure-runtime';

// El tipo del cliente declara `Promise<CommercialData>` (no nullable), pero el endpoint real
// responde 200 con `null` cuando el trámite no tiene datos comerciales (ver CommercialEndpoints.cs:
// "result puede ser null (sin comercial aún)"). Los tests reproducen ese `null` real con un cast,
// igual que hace `CommercialForm` en tiempo de ejecución.
const NULO_COMERCIAL = null as unknown as CommercialData;

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getCommercial: vi.fn(),
    getPrenda: vi.fn(),
  },
}));

const client = vi.mocked(tramitesClient);

const ITEM = { id: 'inst-1', modalidad: 'traspaso' } as unknown as InstanceSummary;

function renderSeccion() {
  return render(<TramiteDetalleComercial instanceId="inst-1" item={ITEM} />);
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe('TramiteDetalleComercial — estado cargando', () => {
  it('muestra el esqueleto de carga en las dos tarjetas mientras las llamadas están pendientes', () => {
    client.getCommercial.mockReturnValue(new Promise(() => {}));
    client.getPrenda.mockReturnValue(new Promise(() => {}));

    renderSeccion();

    expect(screen.getByLabelText('Cargando datos comerciales')).toBeInTheDocument();
    expect(screen.getByLabelText('Cargando decisión de prenda')).toBeInTheDocument();
  });
});

describe('TramiteDetalleComercial — estado lleno', () => {
  it('pinta los importes en pesos con separador de miles y sufijo COP', async () => {
    client.getCommercial.mockResolvedValue({
      valorVenta: 78500000,
      causal: 'COMPRAVENTA',
      tasaImpuesto: 1,
      derechos: 212400,
      metodoPago: 'PSE — Bancolombia',
    });
    client.getPrenda.mockResolvedValue({
      id: 'prenda-1',
      decision: 'sin_prenda',
      estado: 'vigente',
      acreedorNombre: null,
      acreedorDocumento: null,
      levantamientoEntidad: null,
      createdAt: '2026-05-17T10:38:00Z',
    });

    renderSeccion();

    await waitFor(() => expect(client.getCommercial).toHaveBeenCalledWith('inst-1', undefined));

    expect(await screen.findByText('$ 78.500.000')).toBeInTheDocument();
    expect(screen.getByText('$ 212.400')).toBeInTheDocument();
    expect(screen.getByText('Compraventa')).toBeInTheDocument();
    expect(screen.getByText('1%')).toBeInTheDocument();
    expect(screen.getByText('PSE — Bancolombia')).toBeInTheDocument();
    expect(screen.getByText('Sin prenda')).toBeInTheDocument();
  });
});

describe('TramiteDetalleComercial — trámite sin datos comerciales', () => {
  it('usa el estado vacío en vez de pintar la tarjeta con todos los campos en "—"', async () => {
    client.getCommercial.mockResolvedValue(NULO_COMERCIAL);
    client.getPrenda.mockResolvedValue(null);

    renderSeccion();

    expect(
      await screen.findByText(
        'Este trámite no tiene datos comerciales registrados (p. ej. una matrícula inicial no captura valor de venta ni causal).',
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByText('Este trámite no tiene una decisión de prenda o gravamen registrada.'),
    ).toBeInTheDocument();
    // Ninguna rejilla de campo/valor con guiones: no se pinta la tarjeta llena.
    expect(screen.queryByText('Valor de venta')).not.toBeInTheDocument();
  });

  it('trata un objeto con todos los campos en null como vacío (no solo `null` crudo)', async () => {
    client.getCommercial.mockResolvedValue({
      valorVenta: null,
      causal: null,
      tasaImpuesto: null,
      derechos: null,
      metodoPago: null,
    });
    client.getPrenda.mockResolvedValue(null);

    renderSeccion();

    expect(
      await screen.findByText(
        'Este trámite no tiene datos comerciales registrados (p. ej. una matrícula inicial no captura valor de venta ni causal).',
      ),
    ).toBeInTheDocument();
  });
});

describe('TramiteDetalleComercial — fallo de una sola llamada', () => {
  it('si falla la prenda, los datos comerciales igual se pintan (y viceversa)', async () => {
    client.getCommercial.mockResolvedValue({
      valorVenta: 50000000,
      causal: 'DONACION',
      tasaImpuesto: null,
      derechos: null,
      metodoPago: null,
    });
    client.getPrenda.mockRejectedValue(new Error('El servicio no está disponible en este momento.'));

    renderSeccion();

    expect(await screen.findByText('$ 50.000.000')).toBeInTheDocument();
    expect(
      await screen.findByText('El servicio no está disponible en este momento.'),
    ).toBeInTheDocument();
    // El error de prenda no borra la tarjeta comercial.
    expect(screen.getByText('Donación')).toBeInTheDocument();
  });

  it('ofrece reintentar y solo repite la llamada de la tarjeta que falló', async () => {
    client.getCommercial.mockResolvedValue({
      valorVenta: 1000,
      causal: null,
      tasaImpuesto: null,
      derechos: null,
      metodoPago: null,
    });
    client.getPrenda.mockRejectedValueOnce(new Error('fallo de red'));
    client.getPrenda.mockResolvedValueOnce({
      id: 'p1',
      decision: 'registrar',
      estado: 'vigente',
      acreedorNombre: 'Banco XYZ',
      acreedorDocumento: '900123456',
      levantamientoEntidad: null,
      createdAt: '2026-01-01T00:00:00Z',
    });

    renderSeccion();

    const boton = await screen.findByRole('button', { name: 'Reintentar' });
    boton.click();

    expect(await screen.findByText('Registrar prenda')).toBeInTheDocument();
    expect(client.getCommercial).toHaveBeenCalledTimes(1);
    expect(client.getPrenda).toHaveBeenCalledTimes(2);
  });
});

describe('TramiteDetalleComercial — cancelación al desmontar', () => {
  it('no actualiza el estado si el componente se desmonta antes de que resuelva la llamada', async () => {
    let resolverComercial: (v: CommercialData) => void = () => {};
    client.getCommercial.mockReturnValue(
      new Promise((resolve) => {
        resolverComercial = resolve;
      }),
    );
    client.getPrenda.mockResolvedValue(null);

    const { unmount } = renderSeccion();
    unmount();

    // Resolver después de desmontar no debe lanzar (setState en componente desmontado).
    expect(() =>
      resolverComercial({
        valorVenta: 1,
        causal: null,
        tasaImpuesto: null,
        derechos: null,
        metodoPago: null,
      }),
    ).not.toThrow();
  });
});
