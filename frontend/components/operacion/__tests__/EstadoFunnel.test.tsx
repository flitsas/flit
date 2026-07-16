// Funnel de estados del listado de trámites: conteo por estado + filtro (toggle).
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { EstadoFunnel } from '../EstadoFunnel';

const counts = {
  borrador: 5,
  preparado: 2,
  entregado: 3,
  aprobado: 7,
  rechazado: 1,
  anulado: 0,
};

describe('EstadoFunnel', () => {
  it('renderiza un botón por estado con su conteo y nombre accesible', () => {
    render(<EstadoFunnel counts={counts} active="" onSelect={vi.fn()} />);
    const borrador = screen.getByRole('button', { name: 'Borrador' });
    expect(borrador).toBeInTheDocument();
    expect(borrador.textContent).toContain('5');
    expect(screen.getByRole('button', { name: 'Aprobado' }).textContent).toContain('7');
    // HU #10785: los 6 estados de negocio (== develop); la ruta de placa NO añade tarjetas (su
    // progreso es un sub-estado interno que vive bajo 'entregado').
    for (const label of ['Borrador', 'Preparado', 'Entregado', 'Aprobado', 'Rechazado', 'Anulado']) {
      expect(screen.getByRole('button', { name: label })).toBeInTheDocument();
    }
    // preasignado/asignado ya no son tarjetas del funnel.
    expect(screen.queryByRole('button', { name: 'Preasignado' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Asignado' })).toBeNull();
  });

  it('al hacer clic en un estado inactivo lo selecciona como filtro', () => {
    const onSelect = vi.fn();
    render(<EstadoFunnel counts={counts} active="" onSelect={onSelect} />);
    fireEvent.click(screen.getByRole('button', { name: 'Entregado' }));
    expect(onSelect).toHaveBeenCalledWith('entregado');
  });

  it('al hacer clic en el estado ya activo limpia el filtro (toggle) y marca aria-pressed', () => {
    const onSelect = vi.fn();
    render(<EstadoFunnel counts={counts} active="entregado" onSelect={onSelect} />);
    const entregado = screen.getByRole('button', { name: 'Entregado' });
    expect(entregado).toHaveAttribute('aria-pressed', 'true');
    fireEvent.click(entregado);
    expect(onSelect).toHaveBeenCalledWith('');
  });
});
