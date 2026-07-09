import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { StatusHistoryPage } from '@/lib/api/types/procedure-runtime';

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  getStatusHistory: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
}));

import { EstadoTimeline, EstadoTimelinePanel } from '@/components/operacion/EstadoTimeline';

function page(items: StatusHistoryPage['items'], total = items.length): StatusHistoryPage {
  return { items, total, page: 1, pageSize: 20 };
}

const historial = [
  {
    id: 'h3',
    fromStatus: 'entregado',
    toStatus: 'rechazado',
    changedAt: '2026-07-02T15:30:00Z',
    changedByUserId: 'u2',
    changedByName: 'Olga OT',
    reason: 'Documento ilegible',
  },
  {
    id: 'h2',
    fromStatus: 'borrador',
    toStatus: 'preparado',
    changedAt: '2026-07-01T10:00:00Z',
    changedByUserId: 'u1',
    changedByName: 'Ana Gestora',
    reason: null,
  },
  {
    id: 'h1',
    fromStatus: null,
    toStatus: 'borrador',
    changedAt: '2026-06-30T09:00:00Z',
    changedByUserId: null,
    changedByName: null,
    reason: null,
  },
];

beforeEach(() => {
  vi.clearAllMocks();
});

describe('EstadoTimeline', () => {
  it('renderiza las transiciones con labels en español, usuario y motivo', async () => {
    mocks.getStatusHistory.mockResolvedValue(page(historial));

    render(<EstadoTimeline instanceId="inst-1" />);

    // Labels de estado en español (RF01) — destino como chip.
    expect(await screen.findByText('Rechazado')).toBeTruthy();
    expect(screen.getByText('Preparado')).toBeTruthy();
    expect(screen.getByText('Borrador')).toBeTruthy();

    // Origen de la transición y fila inicial (from null).
    expect(screen.getByText('desde Entregado')).toBeTruthy();
    expect(screen.getByText('estado inicial')).toBeTruthy();

    // Usuario y motivo (RF05); proceso automático → '—'.
    expect(screen.getByText(/Olga OT/)).toBeTruthy();
    expect(screen.getByText('Motivo: Documento ilegible')).toBeTruthy();
    expect(screen.getByText(/—/)).toBeTruthy();

    expect(mocks.getStatusHistory).toHaveBeenCalledWith('inst-1', 1, 20);
  });

  it('tolera estados con vocabulario viejo vía fallback titlecase', async () => {
    mocks.getStatusHistory.mockResolvedValue(
      page([
        {
          id: 'h1',
          fromStatus: 'draft',
          toStatus: 'in_review',
          changedAt: '2026-06-30T09:00:00Z',
          changedByUserId: null,
          changedByName: null,
          reason: null,
        },
      ]),
    );

    render(<EstadoTimeline instanceId="inst-1" />);

    expect(await screen.findByText('In review')).toBeTruthy();
    expect(screen.getByText('desde Draft')).toBeTruthy();
  });

  it('muestra vacío cuando no hay transiciones', async () => {
    mocks.getStatusHistory.mockResolvedValue(page([]));

    render(<EstadoTimeline instanceId="inst-1" />);

    expect(
      await screen.findByText('Este trámite aún no tiene cambios de estado.'),
    ).toBeTruthy();
  });

  it('muestra error cuando la carga falla', async () => {
    mocks.getStatusHistory.mockRejectedValue(new Error('boom'));

    render(<EstadoTimeline instanceId="inst-1" />);

    expect(await screen.findByRole('alert')).toBeTruthy();
    expect(screen.getByText('No se pudo cargar el historial de estados.')).toBeTruthy();
  });

  it('pagina con "Ver más" cuando hay más filas que la página', async () => {
    mocks.getStatusHistory.mockResolvedValueOnce({
      items: historial.slice(0, 2),
      total: 3,
      page: 1,
      pageSize: 20,
    });
    mocks.getStatusHistory.mockResolvedValueOnce({
      items: historial.slice(2),
      total: 3,
      page: 2,
      pageSize: 20,
    });

    render(<EstadoTimeline instanceId="inst-1" />);

    const verMas = await screen.findByRole('button', { name: /Ver más/ });
    await userEvent.click(verMas);

    expect(await screen.findByText('Borrador')).toBeTruthy();
    expect(mocks.getStatusHistory).toHaveBeenLastCalledWith('inst-1', 2, 20);
  });
});

describe('EstadoTimelinePanel', () => {
  it('no carga el historial hasta abrir el panel', async () => {
    mocks.getStatusHistory.mockResolvedValue(page(historial));

    render(<EstadoTimelinePanel instanceId="inst-1" />);

    expect(mocks.getStatusHistory).not.toHaveBeenCalled();

    await userEvent.click(
      screen.getByRole('button', { name: /Historial de estados/ }),
    );

    expect(await screen.findByText('Rechazado')).toBeTruthy();
    expect(mocks.getStatusHistory).toHaveBeenCalledWith('inst-1', 1, 20);
  });
});
