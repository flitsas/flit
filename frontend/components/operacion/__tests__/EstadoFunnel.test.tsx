import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { EstadoFunnel } from '../EstadoFunnel';

const counts = {
  borrador: 5,
  preparado: 2,
  entregado: 3,
  aprobado: 7,
  rechazado: 1,
  anulado: 0,
  subsanacion: 4,
};

describe('EstadoFunnel', () => {
  it('renderiza una celda por estado con su conteo y nombre accesible', () => {
    render(<EstadoFunnel counts={counts} />);
    expect(screen.getByLabelText('Borrador: 5 trámites')).toBeInTheDocument();
    expect(screen.getByLabelText('Aprobado: 7 trámites')).toBeInTheDocument();
    for (const label of [
      'Borrador',
      'Preparado',
      'Entregado',
      'Aprobado',
      'En subsanación',
      'Rechazado',
      'Anulado',
    ]) {
      expect(screen.getByLabelText(new RegExp(`^${label}:`))).toBeInTheDocument();
    }
    expect(screen.getByLabelText('En subsanación: 4 trámites')).toBeInTheDocument();
    expect(screen.queryByLabelText(/^Preasignado:/)).toBeNull();
    expect(screen.queryByLabelText(/^Asignado:/)).toBeNull();
  });

  it('singulariza el nombre accesible cuando hay un solo trámite', () => {
    render(<EstadoFunnel counts={counts} />);
    expect(screen.getByLabelText('Rechazado: 1 trámite')).toBeInTheDocument();
  });

  it('filtra al clic y quita el filtro al repetir el mismo estado', async () => {
    const onSelect = vi.fn();
    const user = userEvent.setup();
    const { rerender } = render(
      <EstadoFunnel counts={counts} selected="" onSelect={onSelect} />,
    );

    await user.click(screen.getByRole('button', { name: 'Borrador: 5 trámites' }));
    expect(onSelect).toHaveBeenCalledWith('borrador');

    rerender(<EstadoFunnel counts={counts} selected="borrador" onSelect={onSelect} />);
    expect(screen.getByRole('button', { name: 'Borrador: 5 trámites' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );

    await user.click(screen.getByRole('button', { name: 'Borrador: 5 trámites' }));
    expect(onSelect).toHaveBeenCalledWith('');
  });
});
