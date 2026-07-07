// HU #10596 (R4) — formulario declarativo de prenda en matrícula.
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { PrendaForm } from '../PrendaForm';
import { tramitesClient } from '@/lib/api/tramites-client';

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getPrenda: vi.fn().mockResolvedValue(null),
    putPrenda: vi.fn().mockResolvedValue({
      id: '1',
      decision: 'registrar',
      estado: 'vigente',
      acreedorNombre: 'Banco XYZ',
      acreedorDocumento: null,
      createdAt: '2026-07-07T00:00:00Z',
    }),
  },
}));

const client = vi.mocked(tramitesClient);

describe('PrendaForm (matrícula, R4)', () => {
  beforeEach(() => {
    client.getPrenda.mockClear();
    client.putPrenda.mockClear();
  });

  it('ofrece solo las decisiones de matrícula (registrar / sin prenda)', async () => {
    render(<PrendaForm instanceId="abc" />);
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    expect(screen.getByRole('option', { name: 'Registrar prenda existente' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Sin prenda' })).toBeInTheDocument();
    // Las decisiones de traspaso no aparecen en la variante matrícula.
    expect(screen.queryByRole('option', { name: 'Levantar gravamen' })).not.toBeInTheDocument();
  });

  it('con "sin prenda" no exige documento ni datos del acreedor (puede continuar)', async () => {
    render(<PrendaForm instanceId="abc" />);
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    fireEvent.change(screen.getByLabelText('Decisión de prenda'), {
      target: { value: 'sin_prenda' },
    });

    expect(screen.queryByLabelText('Acreedor (beneficiario)')).not.toBeInTheDocument();
    expect(screen.queryByRole('note')).not.toBeInTheDocument();
  });

  it('con "registrar" pide acreedor y recuerda el documento', async () => {
    render(<PrendaForm instanceId="abc" />);
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    fireEvent.change(screen.getByLabelText('Decisión de prenda'), {
      target: { value: 'registrar' },
    });

    expect(screen.getByLabelText('Acreedor (beneficiario)')).toBeInTheDocument();
    expect(screen.getByRole('note')).toHaveTextContent(/documento de prenda/i);
  });

  it('en traspaso ofrece las 4 decisiones de gestión (sin "sin prenda")', async () => {
    render(
      <PrendaForm
        instanceId="abc"
        decisions={['solicitar', 'registrar', 'levantar', 'omitir']}
      />,
    );
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    expect(screen.getByRole('option', { name: 'Solicitar constitución de prenda' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Levantar gravamen' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Continuar sin gestionar (asumo el riesgo)' })).toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'Sin prenda' })).not.toBeInTheDocument();
  });

  it('guarda la decisión con los datos del acreedor', async () => {
    render(<PrendaForm instanceId="abc" />);
    await waitFor(() => expect(client.getPrenda).toHaveBeenCalled());

    fireEvent.change(screen.getByLabelText('Decisión de prenda'), {
      target: { value: 'registrar' },
    });
    fireEvent.change(screen.getByLabelText('Acreedor (beneficiario)'), {
      target: { value: 'Banco XYZ' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Guardar decisión de prenda/i }));

    await waitFor(() =>
      expect(client.putPrenda).toHaveBeenCalledWith('abc', {
        decision: 'registrar',
        acreedorNombre: 'Banco XYZ',
        acreedorDocumento: null,
      }),
    );
  });
});
