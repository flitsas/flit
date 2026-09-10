'use client';

import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import { TramiteTrackingModal } from '@/components/operacion/TramiteTrackingModal';
import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';

const getStatusHistory = vi.fn();

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getStatusHistory: (...args: unknown[]) => getStatusHistory(...args),
  },
}));

/**
 * HU #12185 — el panel del trámite, abierto desde el indicador de estado del listado.
 *
 * Lo que se fija aquí es que el panel responde las DOS preguntas: «qué trámite es» (la ficha, que
 * se arma con lo que ya viaja en la fila) y «por dónde va» (el historial, con quién movió cada
 * paso y desde qué compañía).
 */
function fila(parcial: Partial<InstanceSummary> = {}): InstanceSummary {
  return {
    id: 'inst-1',
    referenceNumber: 'FT1-0000018',
    modalidad: 'TRASPASO',
    estado: 'entregado',
    placa: 'KYU631',
    vin: 'LRWYGCEK7TC769623',
    vehiculoMarca: 'Renault',
    vehiculoLinea: 'Duster',
    compradorNombre: 'Laura Restrepo Ossa',
    compradorDocumento: '1020998455',
    vendedorNombre: 'Comercializadora del Norte S.A.S',
    vendedorDocumento: '900412887',
    organismoTransito: 'Tránsito de Funza',
    companiaNombre: 'Renting Colombia S.A.S',
    tipoNombre: 'Traspaso',
    pasoActual: 6,
    totalPasos: 6,
    pasoNombre: 'Entrega al organismo',
    createdAt: '2026-08-27T11:41:00Z',
    ...parcial,
  } as InstanceSummary;
}

describe('TramiteTrackingModal', () => {
  beforeEach(() => {
    getStatusHistory.mockReset();
    getStatusHistory.mockResolvedValue({
      items: [
        {
          id: 'h2',
          fromStatus: 'borrador',
          toStatus: 'preparado',
          changedAt: '2026-08-27T15:49:00Z',
          changedByUserId: 'u1',
          changedByName: 'Laura Restrepo',
          changedByCompania: 'Renting Colombia S.A.S',
          reason: null,
        },
        {
          id: 'h1',
          fromStatus: null,
          toStatus: 'borrador',
          changedAt: '2026-08-27T11:41:00Z',
          changedByUserId: 'u2',
          changedByName: 'Usuario Demo',
          changedByCompania: 'Renting Colombia S.A.S',
          reason: null,
        },
      ],
      total: 2,
      page: 1,
      pageSize: 50,
    });
  });

  // ── La ficha ─────────────────────────────────────────────────────────────────────────────

  it('la ficha identifica el trámite sin volver a la fila', async () => {
    render(<TramiteTrackingModal open item={fila()} onClose={() => undefined} />);

    const ficha = await screen.findByRole('region', { name: 'Resumen del trámite' });
    expect(within(ficha).getByText('KYU631')).toBeInTheDocument();
    expect(within(ficha).getByText('LRWYGCEK7TC769623')).toBeInTheDocument();
    expect(within(ficha).getByText('Renault Duster')).toBeInTheDocument();
    expect(within(ficha).getByText('Tránsito de Funza')).toBeInTheDocument();
    expect(within(ficha).getByText('Renting Colombia S.A.S')).toBeInTheDocument();
    expect(within(ficha).getByText('6 de 6 · Entrega al organismo')).toBeInTheDocument();
  });

  it('la ficha no cuesta ninguna consulta: solo se pide el historial', async () => {
    render(<TramiteTrackingModal open item={fila()} onClose={() => undefined} />);

    await screen.findByRole('region', { name: 'Resumen del trámite' });
    await waitFor(() => expect(getStatusHistory).toHaveBeenCalledTimes(1));
    expect(getStatusHistory).toHaveBeenCalledWith('inst-1', 1, 50, undefined);
  });

  it('en traspaso nombra vendedor y comprador, con su documento', async () => {
    render(<TramiteTrackingModal open item={fila()} onClose={() => undefined} />);

    const ficha = await screen.findByRole('region', { name: 'Resumen del trámite' });
    expect(within(ficha).getByText('Vendedor')).toBeInTheDocument();
    expect(
      within(ficha).getByText('Comercializadora del Norte S.A.S · 900412887'),
    ).toBeInTheDocument();
    expect(within(ficha).getByText('Laura Restrepo Ossa · 1020998455')).toBeInTheDocument();
  });

  it('en matrícula inicial la parte es el PROPIETARIO y no hay vendedor', async () => {
    // Llamarlo «comprador» en una matrícula es un error de vocabulario, y una fila «Vendedor: sin
    // asignar» sugeriría un dato pendiente de capturar cuando en ese trámite no existe.
    render(
      <TramiteTrackingModal
        open
        item={fila({ modalidad: 'MATRICULAS', vendedorNombre: null, vendedorDocumento: null })}
        onClose={() => undefined}
      />,
    );

    const ficha = await screen.findByRole('region', { name: 'Resumen del trámite' });
    expect(within(ficha).getByText('Propietario')).toBeInTheDocument();
    expect(within(ficha).queryByText('Vendedor')).not.toBeInTheDocument();
    expect(within(ficha).queryByText('Comprador')).not.toBeInTheDocument();
  });

  it('un dato ausente se dice, no se deja en blanco', async () => {
    render(
      <TramiteTrackingModal open item={fila({ placa: null })} onClose={() => undefined} />,
    );

    const ficha = await screen.findByRole('region', { name: 'Resumen del trámite' });
    expect(within(ficha).getAllByText('Sin asignar').length).toBeGreaterThan(0);
  });

  // ── El historial ─────────────────────────────────────────────────────────────────────────

  it('cada movimiento dice estado, quién y desde qué compañía', async () => {
    render(<TramiteTrackingModal open item={fila()} onClose={() => undefined} />);

    const historial = await screen.findByRole('list', { name: 'Historial de estados del trámite' });
    expect(within(historial).getByText(/Preparado desde Borrador/)).toBeInTheDocument();
    expect(
      within(historial).getByText('Renting Colombia S.A.S · Laura Restrepo'),
    ).toBeInTheDocument();
  });

  it('el movimiento vigente va primero', async () => {
    render(<TramiteTrackingModal open item={fila()} onClose={() => undefined} />);

    const historial = await screen.findByRole('list', { name: 'Historial de estados del trámite' });
    const items = within(historial).getAllByRole('listitem');
    expect(items[0]).toHaveTextContent(/Preparado/);
  });

  it('un movimiento automático, sin usuario ni compañía, no rompe la línea', async () => {
    getStatusHistory.mockResolvedValue({
      items: [
        {
          id: 'h1',
          fromStatus: null,
          toStatus: 'borrador',
          changedAt: '2026-08-27T11:41:00Z',
          changedByUserId: null,
          changedByName: null,
          changedByCompania: null,
          reason: null,
        },
      ],
      total: 1,
      page: 1,
      pageSize: 50,
    });
    render(<TramiteTrackingModal open item={fila()} onClose={() => undefined} />);

    const historial = await screen.findByRole('list', { name: 'Historial de estados del trámite' });
    expect(within(historial).getByText(/Borrador/)).toBeInTheDocument();
  });

  it('sin movimientos lo dice en vez de dejar el hueco', async () => {
    getStatusHistory.mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 50 });
    render(<TramiteTrackingModal open item={fila()} onClose={() => undefined} />);

    expect(await screen.findByText('Todavía no hay movimientos registrados.')).toBeInTheDocument();
  });

  it('si el historial falla, la ficha se sigue leyendo', async () => {
    // El resumen no depende del servidor: perderlo por un fallo de red dejaría el panel inútil.
    getStatusHistory.mockRejectedValue(new Error('sin red'));
    render(<TramiteTrackingModal open item={fila()} onClose={() => undefined} />);

    expect(await screen.findByRole('region', { name: 'Resumen del trámite' })).toBeInTheDocument();
    expect(await screen.findByText(/sin red/)).toBeInTheDocument();
  });

  it('cerrado no consulta nada', async () => {
    render(<TramiteTrackingModal open={false} item={fila()} onClose={() => undefined} />);
    await waitFor(() => expect(getStatusHistory).not.toHaveBeenCalled());
  });

  it('sin fila no pinta nada', () => {
    const { container } = render(
      <TramiteTrackingModal open item={null} onClose={() => undefined} />,
    );
    expect(container).toBeEmptyDOMElement();
  });
});
