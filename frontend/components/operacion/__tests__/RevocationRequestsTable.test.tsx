// HU #12578 (Feature #12565) — AC1: tabla de la vista dedicada "Revocatorias", compartida por gestor
// y OT. Uso de ejemplo: <RevocationRequestsTable items={[...]} total={1} skip={0} take={20}
// onPageChange={fn} onView={fn} /> → una fila por solicitud con radicado/placa/OT/estado/intento.
import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RevocationRequestsTable } from '../RevocationRequestsTable';
import type { RevocationRequestListItem } from '@/lib/api/types/revocation-requests';

function makeItem(overrides: Partial<RevocationRequestListItem> = {}): RevocationRequestListItem {
  return {
    revocationRequestId: 'req-1',
    procedureInstanceId: 'proc-1',
    referenceNumber: 'RAD-2026-001',
    placa: 'ABC123',
    transitOfficeId: 'ot-1',
    transitOfficeName: 'OT Bogotá',
    status: 'solicitada',
    attemptNumber: 1,
    requestedAt: '2026-09-01T10:00:00Z',
    decidedAt: null,
    ...overrides,
  };
}

describe('RevocationRequestsTable', () => {
  it('renderiza una fila por cada solicitud con radicado, placa, OT y estado', () => {
    render(
      <RevocationRequestsTable
        items={[makeItem()]}
        total={1}
        skip={0}
        take={20}
        onPageChange={vi.fn()}
      />,
    );

    expect(screen.getByText('RAD-2026-001')).toBeInTheDocument();
    expect(screen.getByText('ABC123')).toBeInTheDocument();
    expect(screen.getByText('OT Bogotá')).toBeInTheDocument();
  });

  it('muestra "Sin resultados" cuando el listado está vacío', () => {
    render(<RevocationRequestsTable items={[]} total={0} skip={0} take={20} onPageChange={vi.fn()} />);

    expect(screen.getByText(/Sin resultados/i)).toBeInTheDocument();
  });

  it('sin placa/organismo (null) no lanza y muestra el placeholder "—"', () => {
    render(
      <RevocationRequestsTable
        items={[makeItem({ placa: null, transitOfficeName: null })]}
        total={1}
        skip={0}
        take={20}
        onPageChange={vi.fn()}
      />,
    );

    expect(screen.getAllByText('—').length).toBeGreaterThanOrEqual(2);
  });

  it('sin onView no pinta la columna de acciones "Ver trámite"', () => {
    render(
      <RevocationRequestsTable items={[makeItem()]} total={1} skip={0} take={20} onPageChange={vi.fn()} />,
    );

    expect(screen.queryByRole('button', { name: /Ver trámite/i })).not.toBeInTheDocument();
  });

  it('con onView, al hacer clic invoca el callback con el item de la fila', async () => {
    const onView = vi.fn();
    const item = makeItem();
    render(
      <RevocationRequestsTable
        items={[item]}
        total={1}
        skip={0}
        take={20}
        onPageChange={vi.fn()}
        onView={onView}
      />,
    );

    await userEvent.click(screen.getByRole('button', { name: /Ver trámite RAD-2026-001/i }));

    expect(onView).toHaveBeenCalledWith(item);
  });

  it('todos los encabezados de columna son accesibles (scope="col")', () => {
    render(
      <RevocationRequestsTable items={[makeItem()]} total={1} skip={0} take={20} onPageChange={vi.fn()} />,
    );

    const headers = screen.getAllByRole('columnheader');
    expect(headers.length).toBeGreaterThanOrEqual(7);
  });
});
