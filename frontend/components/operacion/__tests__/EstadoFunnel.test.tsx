// Tira de KPIs por estado del listado de trámites: conteo por estado y filtro al clic.
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
    render(<EstadoFunnel counts={counts} onEstadoClick={vi.fn()} />);
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
    render(<EstadoFunnel counts={counts} onEstadoClick={vi.fn()} />);
    expect(screen.getByLabelText('Rechazado: 1 trámite')).toBeInTheDocument();
  });

  it('expone un botón por estado con aria-pressed cuando hay handler', () => {
    render(
      <EstadoFunnel counts={counts} selectedEstado="preparado" onEstadoClick={vi.fn()} />,
    );
    const buttons = screen.getAllByRole('button');
    expect(buttons).toHaveLength(7);
    expect(screen.getByRole('button', { name: 'Preparado: 2 trámites' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    expect(screen.getByRole('button', { name: 'Borrador: 5 trámites' })).toHaveAttribute(
      'aria-pressed',
      'false',
    );
  });

  it('invoca onEstadoClick al pulsar un KPI', async () => {
    const onEstadoClick = vi.fn();
    render(<EstadoFunnel counts={counts} onEstadoClick={onEstadoClick} />);
    await userEvent.click(screen.getByRole('button', { name: 'Entregado: 3 trámites' }));
    expect(onEstadoClick).toHaveBeenCalledTimes(1);
    expect(onEstadoClick).toHaveBeenCalledWith('entregado');
  });

  it('marca aria-pressed en el KPI seleccionado', () => {
    const { rerender } = render(
      <EstadoFunnel counts={counts} selectedEstado="aprobado" onEstadoClick={vi.fn()} />,
    );
    expect(screen.getByRole('button', { name: 'Aprobado: 7 trámites' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    rerender(
      <EstadoFunnel counts={counts} selectedEstado={null} onEstadoClick={vi.fn()} />,
    );
    expect(screen.getByRole('button', { name: 'Aprobado: 7 trámites' })).toHaveAttribute(
      'aria-pressed',
      'false',
    );
  });

  it('sin onEstadoClick los KPIs no son interactivos (disabled, sin aria-pressed)', () => {
    render(<EstadoFunnel counts={counts} />);
    const buttons = screen.getAllByRole('button');
    expect(buttons).toHaveLength(7);
    for (const btn of buttons) {
      expect(btn).toBeDisabled();
      expect(btn).not.toHaveAttribute('aria-pressed');
    }
  });
});
