// HU #10600 (R17) — modificar la elección de prenda desde el detalle del trámite.
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { PrendaModificar } from '../PrendaModificar';
import { tramitesClient } from '@/lib/api/tramites-client';

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getPrenda: vi.fn(),
    putPrenda: vi.fn().mockResolvedValue({}),
  },
}));

const client = vi.mocked(tramitesClient);

describe('PrendaModificar (R17)', () => {
  beforeEach(() => {
    client.getPrenda.mockReset();
    client.putPrenda.mockClear();
  });

  it('no se muestra si el trámite no tiene prenda vigente', async () => {
    client.getPrenda.mockResolvedValue(null);
    const { container } = render(<PrendaModificar instanceId="abc" />);
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());
    expect(container).toBeEmptyDOMElement();
  });

  it('muestra la prenda vigente y la acción de modificar', async () => {
    client.getPrenda.mockResolvedValue({
      id: '1',
      decision: 'registrar',
      estado: 'vigente',
      acreedorNombre: 'Banco XYZ',
      acreedorDocumento: null,
      createdAt: '2026-07-07T00:00:00Z',
    });
    render(<PrendaModificar instanceId="abc" />);

    await waitFor(() =>
      expect(screen.getByText(/Registrar prenda existente/)).toBeInTheDocument(),
    );
    expect(screen.getByText(/Banco XYZ/)).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Modificar elección de prenda' }),
    ).toBeInTheDocument();
  });

  it('al abrir muestra el formulario editable de prenda', async () => {
    client.getPrenda.mockResolvedValue({
      id: '1',
      decision: 'registrar',
      estado: 'vigente',
      acreedorNombre: null,
      acreedorDocumento: null,
      createdAt: '2026-07-07T00:00:00Z',
    });
    render(<PrendaModificar instanceId="abc" />);

    await waitFor(() =>
      expect(
        screen.getByRole('button', { name: 'Modificar elección de prenda' }),
      ).toBeInTheDocument(),
    );
    fireEvent.click(screen.getByRole('button', { name: 'Modificar elección de prenda' }));

    expect(screen.getByLabelText('Decisión de prenda')).toBeInTheDocument();
  });
});
