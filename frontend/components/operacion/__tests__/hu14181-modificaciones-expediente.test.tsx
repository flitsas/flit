// HU #14181 — bitácora de modificaciones dentro del expediente abierto.
// La línea sale de getStatusHistory y se pinta en el orden del API (más reciente primero).
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ExpedienteCronologicoAccordion } from '../ExpedienteTimeline';
import { TramiteTrackingModal } from '@/components/operacion/TramiteTrackingModal';
import { formatFechaHora } from '@/lib/format/date';
import type {
  InstanceSummary,
  StatusHistoryModification,
  StatusHistoryPage,
} from '@/lib/api/types/procedure-runtime';

const mocks = vi.hoisted(() => ({
  getStatusHistory: vi.fn(),
  getInstance: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getStatusHistory: (...args: unknown[]) => mocks.getStatusHistory(...args),
    getInstance: (...args: unknown[]) => mocks.getInstance(...args),
  },
}));

function modificacion(
  parcial: Partial<StatusHistoryModification> & Pick<StatusHistoryModification, 'id' | 'etiqueta' | 'changedAt'>,
): StatusHistoryModification {
  return {
    campo: parcial.etiqueta,
    anterior: '',
    nuevo: '',
    changedByUserId: null,
    changedByName: null,
    changedByCompania: null,
    ...parcial,
  };
}

function pagina(opts: {
  modificaciones?: StatusHistoryModification[] | null;
  modificacionesTotal?: number;
  page?: number;
  items?: StatusHistoryPage['items'];
}): StatusHistoryPage {
  const modificaciones = opts.modificaciones ?? [];
  return {
    items: opts.items ?? [],
    total: opts.items?.length ?? 0,
    page: opts.page ?? 1,
    pageSize: 20,
    modificaciones,
    modificacionesTotal: opts.modificacionesTotal ?? (modificaciones?.length ?? 0),
  };
}

const reciente = modificacion({
  id: 'm-reciente',
  etiqueta: 'Placa',
  campo: 'placa',
  anterior: 'ABC123',
  nuevo: 'XYZ987',
  changedAt: '2026-10-29T20:15:00Z',
  changedByUserId: 'u1',
  changedByName: 'Ana Gestora',
  changedByCompania: 'Renting Colombia S.A.S',
});

const antigua = modificacion({
  id: 'm-antigua',
  etiqueta: 'Color',
  campo: 'color',
  anterior: '',
  nuevo: 'Rojo',
  changedAt: '2026-10-01T15:00:00Z',
  changedByName: null,
});

async function abrirExpediente() {
  const user = userEvent.setup();
  const trigger = screen.getByRole('button', {
    name: /Expandir Expediente — Trazabilidad cronológica/i,
  });
  expect(trigger).toHaveAttribute('aria-expanded', 'false');
  await user.click(trigger);
  return user;
}

beforeEach(() => {
  mocks.getStatusHistory.mockReset();
  mocks.getInstance.mockReset();
  mocks.getInstance.mockResolvedValue({ events: [] });
});

describe('HU #14181 — AC1 modificaciones en el expediente', () => {
  it('con dos modificaciones, la más reciente queda primero y cada fila trae hora, responsable, dato, anterior y nuevo', async () => {
    mocks.getStatusHistory.mockResolvedValue(
      pagina({ modificaciones: [reciente, antigua], modificacionesTotal: 2 }),
    );

    render(<ExpedienteCronologicoAccordion statusHistory={[]} instanceId="inst-1" />);
    expect(mocks.getStatusHistory).not.toHaveBeenCalled();

    await abrirExpediente();

    const lista = await screen.findByRole('list', { name: 'Modificaciones del expediente' });
    const filas = within(lista).getAllByRole('listitem');
    expect(filas).toHaveLength(2);

    expect(filas[0]).toHaveTextContent(formatFechaHora(reciente.changedAt));
    expect(filas[0]).toHaveTextContent('Ana Gestora · Renting Colombia S.A.S');
    expect(filas[0]).toHaveTextContent('Placa');
    expect(filas[0]).toHaveTextContent('ABC123');
    expect(filas[0]).toHaveTextContent('XYZ987');

    expect(filas[1]).toHaveTextContent(formatFechaHora(antigua.changedAt));
    expect(filas[1]).toHaveTextContent('Sistema');
    expect(filas[1]).not.toHaveTextContent('Sistema ·');
    expect(filas[1]).toHaveTextContent('Color');
    expect(within(filas[1]).getByText('—')).toBeInTheDocument();
    expect(filas[1]).toHaveTextContent('Rojo');

    expect(mocks.getStatusHistory).toHaveBeenCalledTimes(1);
    expect(mocks.getStatusHistory).toHaveBeenCalledWith('inst-1', 1, 20);
  });

  it('el estado que ya está en la línea no se repite como hito y se lee en español', async () => {
    mocks.getStatusHistory.mockResolvedValue(
      pagina({
        modificaciones: [
          modificacion({
            id: 'est-1',
            etiqueta: 'Estado',
            campo: 'estado',
            anterior: '',
            nuevo: 'borrador',
            changedAt: '2026-10-08T22:05:00.000Z',
            changedByName: 'Admin Empresa Demo',
            changedByCompania: 'Empresa Demo S.A.S',
          }),
        ],
        modificacionesTotal: 1,
      }),
    );

    render(
      <ExpedienteCronologicoAccordion
        instanceId="inst-1"
        statusHistory={[
          {
            fromStatus: null,
            toStatus: 'borrador',
            changedAt: '2026-10-08T22:05:00.000Z',
            reason: null,
          },
        ]}
      />,
    );
    await abrirExpediente();

    const lista = await screen.findByRole('list', { name: 'Modificaciones del expediente' });
    expect(within(lista).getByText('Borrador')).toBeInTheDocument();
    expect(screen.getAllByRole('list')).toHaveLength(1);
  });

  it('conserva el rótulo que el API ya manda en changedByName', async () => {
    const runt = modificacion({
      id: 'm-runt',
      etiqueta: 'VIN',
      changedAt: '2026-10-29T18:00:00Z',
      changedByName: 'Sistema · RUNT',
      anterior: '1',
      nuevo: '2',
    });
    const identidad = modificacion({
      id: 'm-id',
      etiqueta: 'Documento',
      changedAt: '2026-10-20T18:00:00Z',
      changedByName: 'Sistema · Validación de identidad',
      anterior: 'CC',
      nuevo: 'CE',
    });
    mocks.getStatusHistory.mockResolvedValue(
      pagina({ modificaciones: [runt, identidad], modificacionesTotal: 2 }),
    );

    render(<ExpedienteCronologicoAccordion statusHistory={[]} instanceId="inst-1" />);
    await abrirExpediente();

    const lista = await screen.findByRole('list', { name: 'Modificaciones del expediente' });
    const filas = within(lista).getAllByRole('listitem');
    expect(filas[0]).toHaveTextContent('Sistema · RUNT');
    expect(filas[1]).toHaveTextContent('Sistema · Validación de identidad');
    expect(filas[0]).not.toHaveTextContent('Sistema · RUNT ·');
  });

  it('vacío: Todavía no hay modificaciones', async () => {
    mocks.getStatusHistory.mockResolvedValue(
      pagina({ modificaciones: [], modificacionesTotal: 0 }),
    );

    render(<ExpedienteCronologicoAccordion statusHistory={[]} instanceId="inst-1" />);
    await abrirExpediente();

    expect(await screen.findByText('Todavía no hay modificaciones')).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'Modificaciones del expediente' })).not.toBeInTheDocument();
  });

  it('cargando: anuncia la carga y no muestra el vacío mientras llega', async () => {
    let resolve!: (value: StatusHistoryPage) => void;
    const pendiente = new Promise<StatusHistoryPage>((res) => {
      resolve = res;
    });
    mocks.getStatusHistory.mockReturnValue(pendiente);

    render(<ExpedienteCronologicoAccordion statusHistory={[]} instanceId="inst-1" />);
    await abrirExpediente();

    expect(await screen.findByRole('status', { name: 'Cargando modificaciones' })).toBeInTheDocument();
    expect(screen.queryByText('Todavía no hay modificaciones')).not.toBeInTheDocument();

    resolve(pagina({ modificaciones: [reciente], modificacionesTotal: 1 }));
    expect(await screen.findByText('Placa')).toBeInTheDocument();
    expect(screen.queryByRole('status', { name: 'Cargando modificaciones' })).not.toBeInTheDocument();
  });

  it('error: alerta con Reintentar y al reintentar muestra la fila', async () => {
    mocks.getStatusHistory.mockRejectedValueOnce(new Error('sin red'));
    mocks.getStatusHistory.mockResolvedValueOnce(
      pagina({ modificaciones: [reciente], modificacionesTotal: 1 }),
    );

    render(<ExpedienteCronologicoAccordion statusHistory={[]} instanceId="inst-1" />);
    const user = await abrirExpediente();

    const alerta = await screen.findByRole('alert');
    expect(alerta).toHaveTextContent('No se pudieron cargar las modificaciones.');
    const reintentar = within(alerta).getByRole('button', { name: /Reintentar/ });
    expect(reintentar).toHaveAttribute('type', 'button');
    expect(reintentar.className).toContain('focus-visible:ring-2');
    expect(reintentar.className).toContain('focus-visible:ring-flit-brand');

    await user.click(reintentar);

    expect(await screen.findByText('Placa')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(mocks.getStatusHistory).toHaveBeenCalledTimes(2);
    expect(mocks.getStatusHistory).toHaveBeenLastCalledWith('inst-1', 1, 20);
  });

  it('Ver más pide la página siguiente de 20 y no reordena lo ya mostrado', async () => {
    mocks.getStatusHistory
      .mockResolvedValueOnce(pagina({ modificaciones: [reciente], modificacionesTotal: 2, page: 1 }))
      .mockResolvedValueOnce(pagina({ modificaciones: [antigua], modificacionesTotal: 2, page: 2 }));

    render(<ExpedienteCronologicoAccordion statusHistory={[]} instanceId="inst-1" />);
    const user = await abrirExpediente();

    const verMas = await screen.findByRole('button', { name: 'Ver más' });
    expect(verMas).toHaveAttribute('type', 'button');
    expect(verMas.className).toContain('focus-visible:ring-2');
    expect(verMas.className).toContain('focus-visible:ring-flit-brand');
    expect(mocks.getStatusHistory).toHaveBeenCalledWith('inst-1', 1, 20);

    await user.click(verMas);

    const lista = await screen.findByRole('list', { name: 'Modificaciones del expediente' });
    const filas = within(lista).getAllByRole('listitem');
    expect(filas).toHaveLength(2);
    expect(filas[0]).toHaveTextContent('Placa');
    expect(filas[1]).toHaveTextContent('Color');
    expect(mocks.getStatusHistory).toHaveBeenLastCalledWith('inst-1', 2, 20);
    expect(mocks.getStatusHistory).toHaveBeenCalledTimes(2);
  });
});

describe('HU #14181 — AC2 el panel del listado no vuelca modificaciones', () => {
  it('TramiteTrackingModal sigue en estado y eventos: no pinta valor anterior ni las modificaciones', async () => {
    mocks.getStatusHistory.mockResolvedValue(
      pagina({
        items: [
          {
            id: 'h1',
            fromStatus: 'borrador',
            toStatus: 'preparado',
            changedAt: '2026-08-27T15:49:00Z',
            changedByUserId: 'u1',
            changedByName: 'Laura Restrepo',
            changedByCompania: 'Renting Colombia S.A.S',
            reason: null,
          },
        ],
        modificaciones: [
          modificacion({
            id: 'm1',
            etiqueta: 'Color de carrocería',
            campo: 'color',
            anterior: 'Rojo mate',
            nuevo: 'Azul eléctrico',
            changedAt: '2026-10-29T20:15:00Z',
            changedByName: 'Ana Gestora',
            changedByCompania: 'Renting Colombia S.A.S',
          }),
        ],
        modificacionesTotal: 1,
      }),
    );

    render(
      <TramiteTrackingModal
        open
        item={
          {
            id: 'inst-1',
            referenceNumber: 'FT1-0000018',
            modalidad: 'TRASPASO',
            estado: 'preparado',
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
            pasoActual: 2,
            totalPasos: 6,
            pasoNombre: 'Preparación',
            createdAt: '2026-08-27T11:41:00Z',
          } as InstanceSummary
        }
        onClose={() => undefined}
      />,
    );

    const historial = await screen.findByRole('list', { name: 'Historial de estados del trámite' });
    expect(within(historial).getByText(/Preparado/)).toBeInTheDocument();
    expect(screen.queryByText(/valor anterior/i)).not.toBeInTheDocument();
    expect(screen.queryByText('Color de carrocería')).not.toBeInTheDocument();
    expect(screen.queryByText('Azul eléctrico')).not.toBeInTheDocument();
    expect(screen.queryByText('Rojo mate')).not.toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'Modificaciones del expediente' })).not.toBeInTheDocument();
  });
});
